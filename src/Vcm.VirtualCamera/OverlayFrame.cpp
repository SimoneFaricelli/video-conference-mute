#include "pch.h"
#include "OverlayFrame.h"

using Microsoft::WRL::ComPtr;

namespace vcm
{
    const std::vector<uint8_t>& OverlayFrame::Get(const std::shared_ptr<const OverlayImage>& image, UINT32 width, UINT32 height)
    {
        uint32_t version = image ? image->version : 0;
        if (version == m_version && width == m_width && height == m_height && !m_frame.empty())
        {
            return m_frame;
        }

        m_version = version;
        m_width = width;
        m_height = height;

        std::vector<uint8_t> bgr;
        if (image && !image->bytes.empty() && Decode(image->bytes, width, height, bgr))
        {
            BgrToNv12(bgr, width, height, m_frame);
        }
        else
        {
            Black(width, height, m_frame);
        }
        return m_frame;
    }

    bool OverlayFrame::Decode(const std::vector<uint8_t>& encoded, UINT32 width, UINT32 height, std::vector<uint8_t>& bgr)
    {
        ComPtr<IWICImagingFactory> factory;
        ComPtr<IWICStream> stream;
        ComPtr<IWICBitmapDecoder> decoder;
        ComPtr<IWICBitmapFrameDecode> frame;
        ComPtr<IWICBitmapScaler> scaler;
        ComPtr<IWICFormatConverter> converter;

        HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
        if (SUCCEEDED(hr)) hr = factory->CreateStream(&stream);
        if (SUCCEEDED(hr)) hr = stream->InitializeFromMemory(const_cast<BYTE*>(encoded.data()), static_cast<DWORD>(encoded.size()));
        if (SUCCEEDED(hr)) hr = factory->CreateDecoderFromStream(stream.Get(), nullptr, WICDecodeMetadataCacheOnDemand, &decoder);
        if (SUCCEEDED(hr)) hr = decoder->GetFrame(0, &frame);
        if (SUCCEEDED(hr)) hr = factory->CreateBitmapScaler(&scaler);
        if (SUCCEEDED(hr)) hr = scaler->Initialize(frame.Get(), width, height, WICBitmapInterpolationModeHighQualityCubic);
        if (SUCCEEDED(hr)) hr = factory->CreateFormatConverter(&converter);
        if (SUCCEEDED(hr)) hr = converter->Initialize(scaler.Get(), GUID_WICPixelFormat24bppBGR, WICBitmapDitherTypeNone, nullptr, 0.0,
            WICBitmapPaletteTypeCustom);
        if (SUCCEEDED(hr))
        {
            bgr.resize(static_cast<size_t>(width) * height * 3);
            hr = converter->CopyPixels(nullptr, width * 3, static_cast<UINT>(bgr.size()), bgr.data());
        }

        if (FAILED(hr))
        {
            VCM_LOG(L"Overlay image could not be decoded: 0x%08X", hr);
            return false;
        }
        return true;
    }

    void OverlayFrame::BgrToNv12(const std::vector<uint8_t>& bgr, UINT32 width, UINT32 height, std::vector<uint8_t>& nv12)
    {
        // BT.601 limited range, the usual colorimetry of webcam NV12.
        nv12.resize(static_cast<size_t>(width) * height * 3 / 2);
        uint8_t* y = nv12.data();
        uint8_t* uv = nv12.data() + static_cast<size_t>(width) * height;

        for (UINT32 row = 0; row < height; row++)
        {
            const uint8_t* p = bgr.data() + static_cast<size_t>(row) * width * 3;
            for (UINT32 col = 0; col < width; col++, p += 3)
            {
                int b = p[0], g = p[1], r = p[2];
                y[static_cast<size_t>(row) * width + col] = static_cast<uint8_t>(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
            }
        }

        for (UINT32 row = 0; row < height; row += 2)
        {
            for (UINT32 col = 0; col < width; col += 2)
            {
                int r = 0, g = 0, b = 0;
                for (UINT32 dy = 0; dy < 2; dy++)
                {
                    for (UINT32 dx = 0; dx < 2; dx++)
                    {
                        const uint8_t* p = bgr.data() + (static_cast<size_t>(row + dy) * width + col + dx) * 3;
                        b += p[0];
                        g += p[1];
                        r += p[2];
                    }
                }
                r /= 4;
                g /= 4;
                b /= 4;
                uint8_t* out = uv + static_cast<size_t>(row / 2) * width + col;
                out[0] = static_cast<uint8_t>(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                out[1] = static_cast<uint8_t>(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }
    }

    void OverlayFrame::Black(UINT32 width, UINT32 height, std::vector<uint8_t>& nv12)
    {
        size_t luma = static_cast<size_t>(width) * height;
        nv12.assign(luma * 3 / 2, 128);
        std::fill(nv12.begin(), nv12.begin() + static_cast<ptrdiff_t>(luma), static_cast<uint8_t>(16));
    }

    namespace
    {
        // Locks a buffer for writing/reading and reports its pitch; falls back to a contiguous lock.
        class LockedBuffer
        {
        public:
            HRESULT Lock(IMFMediaBuffer* buffer, LONG defaultPitch, bool write)
            {
                m_buffer = buffer;
                if (SUCCEEDED(buffer->QueryInterface(IID_PPV_ARGS(&m_buffer2D))))
                {
                    BYTE* start = nullptr;
                    DWORD length = 0;
                    if (SUCCEEDED(m_buffer2D->Lock2DSize(write ? MF2DBuffer_LockFlags_Write : MF2DBuffer_LockFlags_Read, &m_scanline0,
                        &m_pitch, &start, &length)))
                    {
                        return S_OK;
                    }
                    m_buffer2D.Reset();
                }

                DWORD maxLength = 0, currentLength = 0;
                RETURN_IF_FAILED(buffer->Lock(&m_scanline0, &maxLength, &currentLength));
                m_pitch = defaultPitch;
                m_locked = true;
                return S_OK;
            }

            ~LockedBuffer()
            {
                if (m_buffer2D) m_buffer2D->Unlock2D();
                else if (m_locked) m_buffer->Unlock();
            }

            BYTE* Data() const { return m_scanline0; }
            LONG Pitch() const { return m_pitch; }

        private:
            IMFMediaBuffer* m_buffer = nullptr;
            ComPtr<IMF2DBuffer2> m_buffer2D;
            BYTE* m_scanline0 = nullptr;
            LONG m_pitch = 0;
            bool m_locked = false;
        };
    }

    HRESULT CopyNv12ToSample(const uint8_t* source, LONG sourcePitch, UINT32 width, UINT32 height, IMFSample* destination)
    {
        ComPtr<IMFMediaBuffer> buffer;
        RETURN_IF_FAILED(destination->GetBufferByIndex(0, &buffer));
        {
            LockedBuffer target;
            RETURN_IF_FAILED(target.Lock(buffer.Get(), static_cast<LONG>(width), true));
            // NV12: luma rows followed by interleaved chroma rows (height / 2) with the same pitch.
            RETURN_IF_FAILED(MFCopyImage(target.Data(), target.Pitch(), source, sourcePitch, width, height));
            RETURN_IF_FAILED(MFCopyImage(target.Data() + static_cast<ptrdiff_t>(target.Pitch()) * height, target.Pitch(),
                source + static_cast<ptrdiff_t>(sourcePitch) * height, sourcePitch, width, height / 2));
        }
        RETURN_IF_FAILED(buffer->SetCurrentLength(width * height * 3 / 2));
        return S_OK;
    }

    HRESULT CopyNv12Sample(IMFSample* source, UINT32 width, UINT32 height, IMFSample* destination)
    {
        ComPtr<IMFMediaBuffer> buffer;
        RETURN_IF_FAILED(source->ConvertToContiguousBuffer(&buffer));
        LockedBuffer input;
        RETURN_IF_FAILED(input.Lock(buffer.Get(), static_cast<LONG>(width), false));
        return CopyNv12ToSample(input.Data(), input.Pitch(), width, height, destination);
    }
}
