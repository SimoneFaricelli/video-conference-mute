#include "pch.h"
#include "MediaStream.h"
#include "CameraReader.h"
#include "OverlayFrame.h"
#include "PipeClient.h"

using Microsoft::WRL::ComPtr;

namespace vcm
{
    namespace
    {
        constexpr ULONGLONG ReopenDelayMs = 2000;

        HRESULT CreateNv12Type(const VideoFormat& format, IMFMediaType** result)
        {
            ComPtr<IMFMediaType> type;
            RETURN_IF_FAILED(MFCreateMediaType(&type));
            RETURN_IF_FAILED(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
            RETURN_IF_FAILED(type->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12));
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE));
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, TRUE));
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_DEFAULT_STRIDE, format.width));
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_SAMPLE_SIZE, format.width * format.height * 3 / 2));
            RETURN_IF_FAILED(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, format.width, format.height));
            RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, format.fpsNumerator, format.fpsDenominator));
            RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
            double bitrate = static_cast<double>(format.width) * format.height * 12 * format.Fps();
            RETURN_IF_FAILED(type->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(std::min<double>(bitrate, UINT32_MAX))));
            *result = type.Detach();
            return S_OK;
        }

        HRESULT SetStreamAttributes(IMFAttributes* attributes, DWORD streamId)
        {
            RETURN_IF_FAILED(attributes->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY, PINNAME_VIDEO_CAPTURE));
            RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_STREAM_ID, streamId));
            RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED, 1));
            RETURN_IF_FAILED(attributes->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES, MFFrameSourceTypes_Color));
            return S_OK;
        }

        VideoFormat FormatOf(IMFMediaType* type)
        {
            VideoFormat format;
            MFGetAttributeSize(type, MF_MT_FRAME_SIZE, &format.width, &format.height);
            if (FAILED(MFGetAttributeRatio(type, MF_MT_FRAME_RATE, &format.fpsNumerator, &format.fpsDenominator)) || format.fpsNumerator == 0)
            {
                format.fpsNumerator = 30;
                format.fpsDenominator = 1;
            }
            return format;
        }
    }

    HRESULT MediaStream::RuntimeClassInitialize(IMFMediaSource* parent, DWORD streamId, const std::vector<VideoFormat>& formats,
        const std::wstring& initialLink)
    {
        m_parent = parent;
        m_streamId = streamId;
        m_initialLink = initialLink;
        m_wake.create(wil::EventOptions::None);

        std::vector<ComPtr<IMFMediaType>> types;
        for (const auto& format : formats)
        {
            ComPtr<IMFMediaType> type;
            RETURN_IF_FAILED(CreateNv12Type(format, &type));
            types.push_back(type);
        }
        RETURN_HR_IF(E_UNEXPECTED, types.empty());

        std::vector<IMFMediaType*> raw;
        for (auto& type : types) raw.push_back(type.Get());

        RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 10));
        RETURN_IF_FAILED(SetStreamAttributes(m_attributes.Get(), streamId));
        RETURN_IF_FAILED(MFCreateEventQueue(&m_eventQueue));
        RETURN_IF_FAILED(MFCreateStreamDescriptor(streamId, static_cast<DWORD>(raw.size()), raw.data(), &m_descriptor));

        ComPtr<IMFMediaTypeHandler> handler;
        RETURN_IF_FAILED(m_descriptor->GetMediaTypeHandler(&handler));
        RETURN_IF_FAILED(handler->SetCurrentMediaType(raw[0]));
        RETURN_IF_FAILED(SetStreamAttributes(m_descriptor.Get(), streamId));
        return S_OK;
    }

    HRESULT MediaStream::CheckShutdown() const
    {
        return m_shutdown ? MF_E_SHUTDOWN : S_OK;
    }

    IFACEMETHODIMP MediaStream::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->BeginGetEvent(callback, state);
    }

    IFACEMETHODIMP MediaStream::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->EndGetEvent(result, event);
    }

    IFACEMETHODIMP MediaStream::GetEvent(DWORD flags, IMFMediaEvent** event)
    {
        ComPtr<IMFMediaEventQueue> queue;
        {
            std::lock_guard lock(m_lock);
            RETURN_IF_FAILED(CheckShutdown());
            queue = m_eventQueue;
        }
        // May block: the lock is not held.
        return queue->GetEvent(flags, event);
    }

    IFACEMETHODIMP MediaStream::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* value)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_eventQueue->QueueEventParamVar(type, extendedType, status, value);
    }

    IFACEMETHODIMP MediaStream::GetMediaSource(IMFMediaSource** source)
    {
        RETURN_HR_IF_NULL(E_POINTER, source);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_parent.CopyTo(source);
    }

    IFACEMETHODIMP MediaStream::GetStreamDescriptor(IMFStreamDescriptor** descriptor)
    {
        RETURN_HR_IF_NULL(E_POINTER, descriptor);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return m_descriptor.CopyTo(descriptor);
    }

    IFACEMETHODIMP MediaStream::RequestSample(IUnknown* token)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        RETURN_HR_IF(MF_E_INVALIDREQUEST, m_state != MF_STREAM_STATE_RUNNING);
        m_requests.emplace_back(token);
        m_wake.SetEvent();
        return S_OK;
    }

    IFACEMETHODIMP MediaStream::SetStreamState(MF_STREAM_STATE state)
    {
        std::unique_lock lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        if (state == m_state)
        {
            return S_OK;
        }

        switch (state)
        {
        case MF_STREAM_STATE_PAUSED:
            RETURN_HR_IF(MF_E_INVALID_STATE_TRANSITION, m_state != MF_STREAM_STATE_RUNNING);
            m_state = MF_STREAM_STATE_PAUSED;
            return S_OK;
        case MF_STREAM_STATE_RUNNING:
            return StartInternal(lock, nullptr, false);
        case MF_STREAM_STATE_STOPPED:
            StopInternal();
            StopPump(lock);
            return S_OK;
        default:
            return MF_E_INVALID_STATE_TRANSITION;
        }
    }

    IFACEMETHODIMP MediaStream::GetStreamState(MF_STREAM_STATE* state)
    {
        RETURN_HR_IF_NULL(E_POINTER, state);
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        *state = m_state;
        return S_OK;
    }

    HRESULT MediaStream::Start(IMFMediaType* type)
    {
        RETURN_HR_IF_NULL(E_INVALIDARG, type);
        std::unique_lock lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        return StartInternal(lock, type, true);
    }

    HRESULT MediaStream::StartInternal(std::unique_lock<std::mutex>& lock, IMFMediaType* type, bool sendEvent)
    {
        WaitForPumpStopped(lock);
        RETURN_IF_FAILED(CheckShutdown());
        if (type)
        {
            m_mediaType = type;
        }
        RETURN_HR_IF_NULL(MF_E_NOT_INITIALIZED, m_mediaType);

        // A pump running (or paused) with another format or allocator is restarted so the camera is reopened.
        BOOL sameType = FALSE;
        if (m_pump.joinable() &&
            (m_pumpAllocator != m_allocator ||
             FAILED(m_pumpType->Compare(m_mediaType.Get(), MF_ATTRIBUTES_MATCH_ALL_ITEMS, &sameType)) || !sameType))
        {
            StopPump(lock);
            RETURN_IF_FAILED(CheckShutdown());
        }

        if (!m_pump.joinable())
        {
            if (!m_allocator)
            {
                RETURN_IF_FAILED(MFCreateVideoSampleAllocatorEx(IID_PPV_ARGS(&m_allocator)));
            }
            RETURN_IF_FAILED(m_allocator->InitializeSampleAllocator(10, m_mediaType.Get()));
            StartPump();
        }

        if (!m_counted)
        {
            m_counted = true;
            PipeClient::Instance().StreamStarted();
        }

        m_state = MF_STREAM_STATE_RUNNING;
        if (sendEvent)
        {
            RETURN_IF_FAILED(m_eventQueue->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, nullptr));
        }
        return S_OK;
    }

    HRESULT MediaStream::Stop(bool sendEvent)
    {
        std::unique_lock lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        StopInternal();
        StopPump(lock);
        if (sendEvent)
        {
            RETURN_IF_FAILED(m_eventQueue->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr));
        }
        return S_OK;
    }

    void MediaStream::StopInternal()
    {
        m_state = MF_STREAM_STATE_STOPPED;
        m_requests.clear();
        if (m_counted)
        {
            m_counted = false;
            PipeClient::Instance().StreamStopped();
        }
    }

    HRESULT MediaStream::Shutdown()
    {
        std::unique_lock lock(m_lock);
        if (m_shutdown)
        {
            return S_OK;
        }
        StopInternal();
        StopPump(lock);
        WaitForPumpStopped(lock);
        m_shutdown = true;
        if (m_eventQueue)
        {
            m_eventQueue->Shutdown();
        }
        m_parent.Reset();
        m_allocator.Reset();
        return S_OK;
    }

    HRESULT MediaStream::SetAllocator(IMFVideoSampleAllocator* allocator)
    {
        std::lock_guard lock(m_lock);
        RETURN_IF_FAILED(CheckShutdown());
        RETURN_HR_IF(MF_E_INVALIDREQUEST, m_state == MF_STREAM_STATE_RUNNING);
        m_allocator = allocator;
        return S_OK;
    }

    void MediaStream::StartPump()
    {
        auto stop = std::make_shared<wil::unique_event>(wil::EventOptions::ManualReset);
        m_stopPump = stop;
        m_pumpType = m_mediaType;
        m_pumpAllocator = m_allocator;
        m_pump = std::thread([this, format = FormatOf(m_mediaType.Get()), allocator = m_allocator, stop]
        {
            Pump(format, allocator, stop);
        });
    }

    void MediaStream::StopPump(std::unique_lock<std::mutex>& lock)
    {
        if (!m_pump.joinable())
        {
            return;
        }
        m_stopPump->SetEvent();
        std::thread pump = std::move(m_pump);
        m_pumpStopping = true;
        // The pump takes m_lock to deliver samples; release it while waiting.
        lock.unlock();
        pump.join();
        lock.lock();
        m_pumpStopping = false;
        m_pumpType.Reset();
        m_pumpAllocator.Reset();
        m_pumpStopped.notify_all();
    }

    void MediaStream::WaitForPumpStopped(std::unique_lock<std::mutex>& lock)
    {
        m_pumpStopped.wait(lock, [this] { return !m_pumpStopping; });
    }

    void MediaStream::Pump(VideoFormat format, ComPtr<IMFVideoSampleAllocator> allocator, std::shared_ptr<wil::unique_event> stop)
    {
        auto coInit = wil::CoInitializeEx(COINIT_MULTITHREADED);
        VCM_LOG(L"Streaming %ux%u@%.2f", format.width, format.height, format.Fps());

        wil::unique_event frameEvent(wil::EventOptions::None);
        CameraReader camera;
        OverlayFrame overlay;
        const ULONGLONG intervalMs = std::max<ULONGLONG>(1, static_cast<ULONGLONG>(format.FrameDuration() / 10'000));
        ULONGLONG lastDelivery = 0;
        ULONGLONG nextOpenAttempt = 0;
        ULONGLONG nextCameraSearch = 0;
        bool lastMuted = false;

        HANDLE handles[] = { stop->get(), frameEvent.get(), m_wake.get() };
        for (;;)
        {
            DWORD wait = WaitForMultipleObjects(ARRAYSIZE(handles), handles, FALSE, 15);
            if (wait == WAIT_OBJECT_0)
            {
                break;
            }

            ULONGLONG now = GetTickCount64();
            CameraState state = PipeClient::Instance().GetState();
            // Never wrap a virtual camera (including this one): that would make the source open itself.
            std::wstring link = !state.sourceLink.empty() && !IsVirtualCamera(state.sourceLink) ? state.sourceLink : m_initialLink;
            if (IsVirtualCamera(link))
            {
                link.clear();
            }
            if (link.empty() && now >= nextCameraSearch)
            {
                link = FindFirstPhysicalCamera();
                m_initialLink = link;
                nextCameraSearch = now + ReopenDelayMs;
            }

            if (state.muted != lastMuted)
            {
                VCM_LOG(L"Camera %s", state.muted ? L"muted" : L"unmuted");
                lastMuted = state.muted;
            }

            // Physical camera: closed while muted with power-off, reopened on unmute or camera change.
            bool wantCamera = !(state.muted && state.powerOffWhenMuted) && !link.empty();
            if (camera.IsOpen() && (!wantCamera || camera.Link() != link || camera.HasFailed()))
            {
                if (camera.HasFailed()) nextOpenAttempt = now + ReopenDelayMs;
                camera.Close();
            }
            if (wantCamera && !camera.IsOpen() && now >= nextOpenAttempt)
            {
                if (FAILED(camera.Open(link, format, frameEvent.get())))
                {
                    camera.Close();
                    nextOpenAttempt = now + ReopenDelayMs;
                }
            }

            ComPtr<IMFSample> frame = camera.Take();
            bool live = !state.muted && camera.IsOpen() && camera.HasDelivered();

            if (live)
            {
                // Real frames are paced by the camera.
                if (frame && Deliver(allocator.Get(), format, frame.Get(), nullptr))
                {
                    lastDelivery = now;
                }
            }
            else if (now - lastDelivery >= intervalMs)
            {
                // Muted, camera warming up after unmute, or camera unavailable: overlay at the stream frame rate.
                const auto& picture = overlay.Get(PipeClient::Instance().GetOverlay(), format.width, format.height);
                if (Deliver(allocator.Get(), format, nullptr, &picture))
                {
                    lastDelivery = now;
                }
            }
        }

        camera.Close();
        VCM_LOG(L"Streaming stopped");
    }

    bool MediaStream::Deliver(IMFVideoSampleAllocator* allocator, const VideoFormat& format, IMFSample* cameraFrame,
        const std::vector<uint8_t>* overlay)
    {
        ComPtr<IUnknown> token;
        {
            std::lock_guard lock(m_lock);
            if (m_shutdown || m_state != MF_STREAM_STATE_RUNNING || m_requests.empty())
            {
                return false;
            }
            token = std::move(m_requests.front());
            m_requests.pop_front();
        }

        ComPtr<IMFSample> sample;
        HRESULT hr = allocator->AllocateSample(&sample);
        if (SUCCEEDED(hr))
        {
            hr = cameraFrame
                ? CopyNv12Sample(cameraFrame, format.width, format.height, sample.Get())
                : CopyNv12ToSample(overlay->data(), static_cast<LONG>(format.width), format.width, format.height, sample.Get());
        }
        if (SUCCEEDED(hr)) hr = sample->SetSampleTime(MFGetSystemTime());
        if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(format.FrameDuration());
        if (SUCCEEDED(hr) && token) hr = sample->SetUnknown(MFSampleExtension_Token, token.Get());

        std::lock_guard lock(m_lock);
        if (hr == MF_E_SAMPLEALLOCATOR_EMPTY)
        {
            // The client still holds every buffer: retry with the next frame.
            if (m_state == MF_STREAM_STATE_RUNNING) m_requests.push_front(std::move(token));
            return false;
        }
        if (FAILED(hr))
        {
            // Drop this request rather than retrying forever; the next request gets a fresh attempt.
            VCM_LOG(L"Frame delivery failed: 0x%08X", hr);
            return false;
        }
        if (m_shutdown || m_state != MF_STREAM_STATE_RUNNING)
        {
            return false;
        }
        return SUCCEEDED(m_eventQueue->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get()));
    }
}
