using System.Runtime.InteropServices;

namespace Vcm.App.Native;

/// <summary>Exports of VcmVirtualCamera.dll (see src/Vcm.VirtualCamera/Exports.cpp).</summary>
internal static unsafe class VirtualCameraNative
{
    public const string DllName = "VcmVirtualCamera.dll";

    /// <summary>CLSID of the media source; the installer registers it under HKLM\SOFTWARE\Classes\CLSID.</summary>
    public const string MediaSourceClsid = "{D0AB47C7-9EBA-4D7A-AF72-4769A4D62109}";

    /// <summary>Name under which the virtual camera appears in other applications.</summary>
    public const string CameraName = "VideoConference Mute";

    [DllImport(DllName, ExactSpelling = true)]
    public static extern int VcmCam_IsSupported();

    [DllImport(DllName, ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int VcmCam_Start(string friendlyName, string sourceSymbolicLink);

    [DllImport(DllName, ExactSpelling = true)]
    public static extern int VcmCam_Stop();

    [DllImport(DllName, ExactSpelling = true)]
    public static extern int VcmCam_EnumerateCameras(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> callback, IntPtr context);
}
