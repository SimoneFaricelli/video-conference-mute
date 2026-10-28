#include "pch.h"
#include "CameraReader.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace vcm
{
    HRESULT FrameSlot::RuntimeClassInitialize(HANDLE frameEvent)
    {
        HANDLE duplicate = nullptr;
        RETURN_IF_WIN32_BOOL_FALSE(DuplicateHandle(GetCurrentProcess(), frameEvent, GetCurrentProcess(), &duplicate, 0, FALSE,
            DUPLICATE_SAME_ACCESS));
        m_frameEvent.reset(duplicate);
        return S_OK;
    }

    void FrameSlot::Activate(IMFSourceReader* reader)
    {
        std::lock_guard lock(m_lock);
        m_reader = reader;
        m_active = true;
    }

    void FrameSlot::Deactivate()
    {
        std::lock_guard lock(m_lock);
        m_active = false;
        m_reader.Reset(); // breaks the reader -> callback -> reader cycle
        m_latest.Reset();
    }

    ComPtr<IMFSample> FrameSlot::Take()
    {
        std::lock_guard lock(m_lock);
        return std::move(m_latest);
    }

    bool FrameSlot::HasDelivered()
    {
        std::lock_guard lock(m_lock);
        return m_delivered;
    }

    bool FrameSlot::HasFailed()
    {
        std::lock_guard lock(m_lock);
        return m_failed;
    }

    IFACEMETHODIMP FrameSlot::OnReadSample(HRESULT status, DWORD, DWORD streamFlags, LONGLONG, IMFSample* sample)
    {
        // The lock is held while issuing the next read so Deactivate() cannot release the reader underneath us.
        std::lock_guard lock(m_lock);
        if (!m_active)
        {
            return S_OK;
        }

        if (FAILED(status) || (streamFlags & (MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_ENDOFSTREAM)))
        {
            VCM_LOG(L"Camera stream ended: hr=0x%08X flags=0x%X", status, streamFlags);
            m_failed = true;
            SetEvent(m_frameEvent.get());
            return S_OK;
        }

        if (sample)
        {
            m_latest = sample;
            m_delivered = true;
            SetEvent(m_frameEvent.get());
        }

        HRESULT hr = m_reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, nullptr, nullptr, nullptr, nullptr);
        if (FAILED(hr))
        {
            VCM_LOG(L"ReadSample failed: 0x%08X", hr);
            m_failed = true;
            SetEvent(m_frameEvent.get());
        }
        return S_OK;
    }

    namespace
    {
        // Native format closest to the requested one: same size first, then frame rate, then cheaper decoding.
        HRESULT SelectNativeType(IMFSourceReader* reader, const VideoFormat& wanted, IMFMediaType** best)
        {
            ComPtr<IMFMediaType> bestType;
            double bestScore = 1e300;
            for (DWORD i = 0;; i++)
            {
                ComPtr<IMFMediaType> type;
                if (FAILED(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), i, &type)))
                {
                    break;
                }

                VideoFormat format;
                GUID subtype{};
                if (FAILED(MFGetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, &format.width, &format.height)) ||
                    FAILED(MFGetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, &format.fpsNumerator, &format.fpsDenominator)) ||
                    FAILED(type->GetGUID(MF_MT_SUBTYPE, &subtype)))
                {
                    continue;
                }

                double sizeDelta = std::abs(static_cast<double>(format.width) * format.height - static_cast<double>(wanted.width) * wanted.height);
                bool smaller = format.width < wanted.width || format.height < wanted.height;
                double fpsDelta = std::abs(format.Fps() - wanted.Fps());
                double score = sizeDelta / 1000.0 + (smaller ? 1e7 : 0) + fpsDelta * 1000.0;
                if (subtype == MFVideoFormat_MJPG) score += 1; // prefer uncompressed at equal size and rate
                if (score < bestScore)
                {
                    bestScore = score;
                    bestType = type;
                }
            }

            RETURN_HR_IF_NULL(MF_E_INVALIDMEDIATYPE, bestType);
            *best = bestType.Detach();
            return S_OK;
        }
    }

    HRESULT CameraReader::Open(const std::wstring& symbolicLink, const VideoFormat& format, HANDLE frameEvent)
    {
        Close();
        m_link = symbolicLink;
        RETURN_HR_IF(HRESULT_FROM_WIN32(ERROR_NOT_FOUND), symbolicLink.empty());

        ComPtr<FrameSlot> slot;
        RETURN_IF_FAILED(MakeAndInitialize<FrameSlot>(&slot, frameEvent));

        ComPtr<IMFMediaSource> device;
        RETURN_IF_FAILED(CreateCameraSource(symbolicLink, &device));
        auto shutdownOnFailure = wil::scope_exit([&] { device->Shutdown(); });

        ComPtr<IMFAttributes> attributes;
        RETURN_IF_FAILED(MFCreateAttributes(&attributes, 3));
        RETURN_IF_FAILED(attributes->SetUnknown(MF_SOURCE_READER_ASYNC_CALLBACK, slot.Get()));
        // Lets the reader decode MJPG/YUY2 and resize to the format requested by the client.
        RETURN_IF_FAILED(attributes->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE));
        RETURN_IF_FAILED(attributes->SetUINT32(MF_SOURCE_READER_DISCONNECT_MEDIASOURCE_ON_SHUTDOWN, TRUE));

        ComPtr<IMFSourceReader> reader;
        RETURN_IF_FAILED(MFCreateSourceReaderFromMediaSource(device.Get(), attributes.Get(), &reader));

        ComPtr<IMFMediaType> native;
        if (SUCCEEDED(SelectNativeType(reader.Get(), format, &native)))
        {
            ComPtr<IMFSourceReaderEx> readerEx;
            DWORD flags = 0;
            if (SUCCEEDED(reader.As(&readerEx)))
            {
                LOG_IF_FAILED(readerEx->SetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), native.Get(), &flags));
            }
        }

        ComPtr<IMFMediaType> output;
        RETURN_IF_FAILED(MFCreateMediaType(&output));
        RETURN_IF_FAILED(output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
        RETURN_IF_FAILED(output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12));
        RETURN_IF_FAILED(output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
        RETURN_IF_FAILED(MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, format.width, format.height));
        RETURN_IF_FAILED(MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, format.fpsNumerator, format.fpsDenominator));
        RETURN_IF_FAILED(MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
        RETURN_IF_FAILED(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), nullptr, output.Get()));

        slot->Activate(reader.Get());
        auto deactivateOnFailure = wil::scope_exit([&] { slot->Deactivate(); });
        RETURN_IF_FAILED(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, nullptr, nullptr, nullptr, nullptr));

        deactivateOnFailure.release();
        shutdownOnFailure.release();
        m_device = device;
        m_reader = reader;
        m_slot = slot;
        VCM_LOG(L"Camera opened: %s at %ux%u@%.2f", symbolicLink.c_str(), format.width, format.height, format.Fps());
        return S_OK;
    }

    void CameraReader::Close()
    {
        if (m_slot)
        {
            m_slot->Deactivate();
            m_slot.Reset();
        }
        m_reader.Reset();
        if (m_device)
        {
            m_device->Shutdown();
            m_device.Reset();
            VCM_LOG(L"Camera closed: %s", m_link.c_str());
        }
    }
}
