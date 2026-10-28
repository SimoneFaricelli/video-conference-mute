#include "pch.h"
#include "CameraUtil.h"

namespace vcm
{
    namespace
    {
        constexpr double MinimumFps = 15.0;
        constexpr double MaximumLegacyFps = 30.0 + 0.01;

        std::wstring GetString(IMFActivate* activate, REFGUID key)
        {
            wil::unique_cotaskmem_string value;
            UINT32 length = 0;
            if (SUCCEEDED(activate->GetAllocatedString(key, &value, &length)) && value)
            {
                return value.get();
            }
            return {};
        }

        std::vector<VideoFormat> DefaultFormats()
        {
            return { { 1920, 1080, 30, 1 }, { 1280, 720, 30, 1 }, { 640, 480, 30, 1 } };
        }
    }

    std::vector<CameraDevice> EnumerateCameras()
    {
        std::vector<CameraDevice> cameras;
        Microsoft::WRL::ComPtr<IMFAttributes> attributes;
        if (FAILED(MFCreateAttributes(&attributes, 1)) ||
            FAILED(attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID)))
        {
            return cameras;
        }

        IMFActivate** devices = nullptr;
        UINT32 count = 0;
        if (FAILED(MFEnumDeviceSources(attributes.Get(), &devices, &count)))
        {
            return cameras;
        }

        for (UINT32 i = 0; i < count; i++)
        {
            cameras.push_back({ GetString(devices[i], MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME),
                GetString(devices[i], MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK) });
            devices[i]->Release();
        }
        CoTaskMemFree(devices);
        return cameras;
    }

    bool IsVirtualCamera(const std::wstring& symbolicLink)
    {
        std::wstring upper = symbolicLink;
        std::transform(upper.begin(), upper.end(), upper.begin(), ::towupper);
        return upper.find(L"VCAMDEVAPI") != std::wstring::npos;
    }

    std::wstring FindFirstPhysicalCamera()
    {
        for (const auto& camera : EnumerateCameras())
        {
            if (!IsVirtualCamera(camera.symbolicLink))
            {
                return camera.symbolicLink;
            }
        }
        return {};
    }

    HRESULT CreateCameraSource(const std::wstring& symbolicLink, IMFMediaSource** source)
    {
        Microsoft::WRL::ComPtr<IMFAttributes> attributes;
        RETURN_IF_FAILED(MFCreateAttributes(&attributes, 2));
        RETURN_IF_FAILED(attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));
        RETURN_IF_FAILED(attributes->SetString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, symbolicLink.c_str()));
        RETURN_IF_FAILED(MFCreateDeviceSource(attributes.Get(), source));
        return S_OK;
    }

    std::vector<VideoFormat> GetOutputFormats(const std::wstring& symbolicLink)
    {
        if (symbolicLink.empty())
        {
            return DefaultFormats();
        }

        Microsoft::WRL::ComPtr<IMFMediaSource> source;
        if (FAILED(CreateCameraSource(symbolicLink, &source)))
        {
            VCM_LOG(L"Cannot open %s to read its formats", symbolicLink.c_str());
            return DefaultFormats();
        }

        std::vector<VideoFormat> formats;
        Microsoft::WRL::ComPtr<IMFPresentationDescriptor> presentation;
        Microsoft::WRL::ComPtr<IMFStreamDescriptor> stream;
        Microsoft::WRL::ComPtr<IMFMediaTypeHandler> handler;
        BOOL selected = FALSE;
        DWORD typeCount = 0;
        if (SUCCEEDED(source->CreatePresentationDescriptor(&presentation)) &&
            SUCCEEDED(presentation->GetStreamDescriptorByIndex(0, &selected, &stream)) &&
            SUCCEEDED(stream->GetMediaTypeHandler(&handler)) &&
            SUCCEEDED(handler->GetMediaTypeCount(&typeCount)))
        {
            for (DWORD i = 0; i < typeCount; i++)
            {
                Microsoft::WRL::ComPtr<IMFMediaType> type;
                VideoFormat format;
                if (FAILED(handler->GetMediaTypeByIndex(i, &type)) ||
                    FAILED(MFGetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, &format.width, &format.height)) ||
                    FAILED(MFGetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, &format.fpsNumerator, &format.fpsDenominator)))
                {
                    continue;
                }
                // NV12 needs even dimensions.
                if (format.width == 0 || format.height == 0 || format.width % 2 || format.height % 2 || format.Fps() < MinimumFps)
                {
                    continue;
                }

                // Per resolution keep the highest rate up to 30 fps (the legacy camera profile, used by most apps,
                // filters out faster types); if a size only exists above 30 fps keep its slowest rate.
                auto better = [](const VideoFormat& candidate, const VideoFormat& current)
                {
                    bool candidateLegacy = candidate.Fps() <= MaximumLegacyFps;
                    bool currentLegacy = current.Fps() <= MaximumLegacyFps;
                    if (candidateLegacy != currentLegacy) return candidateLegacy;
                    return candidateLegacy ? candidate.Fps() > current.Fps() : candidate.Fps() < current.Fps();
                };
                auto existing = std::find_if(formats.begin(), formats.end(),
                    [&](const VideoFormat& f) { return f.width == format.width && f.height == format.height; });
                if (existing == formats.end())
                {
                    formats.push_back(format);
                }
                else if (better(format, *existing))
                {
                    *existing = format;
                }
            }
        }
        source->Shutdown();

        if (formats.empty())
        {
            return DefaultFormats();
        }

        // Largest legacy-compatible format first: it becomes the default media type.
        std::sort(formats.begin(), formats.end(), [](const VideoFormat& a, const VideoFormat& b)
        {
            bool aLegacy = a.Fps() <= MaximumLegacyFps, bLegacy = b.Fps() <= MaximumLegacyFps;
            if (aLegacy != bLegacy) return aLegacy;
            return static_cast<UINT64>(a.width) * a.height > static_cast<UINT64>(b.width) * b.height;
        });
        constexpr size_t MaxFormats = 16;
        if (formats.size() > MaxFormats) formats.resize(MaxFormats);
        return formats;
    }
}
