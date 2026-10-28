#pragma once

namespace vcm
{
    struct VideoFormat
    {
        UINT32 width = 0;
        UINT32 height = 0;
        UINT32 fpsNumerator = 30;
        UINT32 fpsDenominator = 1;

        double Fps() const { return fpsDenominator ? static_cast<double>(fpsNumerator) / fpsDenominator : 0.0; }
        LONGLONG FrameDuration() const { return fpsNumerator ? static_cast<LONGLONG>(10'000'000.0 * fpsDenominator / fpsNumerator) : 333'333; }
    };

    struct CameraDevice
    {
        std::wstring name;
        std::wstring symbolicLink;
    };

    // Physical and virtual video capture devices as seen by Media Foundation.
    std::vector<CameraDevice> EnumerateCameras();

    // True for software (virtual) cameras created through MFCreateVirtualCamera.
    bool IsVirtualCamera(const std::wstring& symbolicLink);

    // First physical camera, used when no camera was chosen yet.
    std::wstring FindFirstPhysicalCamera();

    // Formats offered by the virtual camera for a given physical camera: one per resolution (highest frame rate),
    // at least 15 fps, largest first. Falls back to common sizes when the camera cannot be queried.
    std::vector<VideoFormat> GetOutputFormats(const std::wstring& symbolicLink);

    HRESULT CreateCameraSource(const std::wstring& symbolicLink, IMFMediaSource** source);
}
