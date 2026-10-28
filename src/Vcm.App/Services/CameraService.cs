using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.Services;

internal sealed record CameraInfo(string Name, string SymbolicLink);

internal enum CameraAvailability
{
    Available,
    UnsupportedOs,
    NotInstalled,
}

/// <summary>
/// Owns the Media Foundation virtual camera "VideoConference Mute" and keeps its media source (running inside the
/// Windows Frame Server) informed about mute state, overlay image and source camera through <see cref="CameraPipeServer"/>.
/// </summary>
internal sealed class CameraService : ICameraController, IDisposable
{
    private const long MaxOverlaySize = 16 * 1024 * 1024;

    private readonly Dispatcher _dispatcher;
    private CameraPipeServer? _pipe;
    private bool _muted;
    private bool _powerOffWhenMuted = true;
    private string _sourceSymbolicLink = string.Empty;
    private string _overlayPath = string.Empty;
    private uint _overlayVersion;
    private bool _started;

    public CameraService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Availability = DetectAvailability();
        Logger.Info($"Virtual camera availability: {Availability}");
    }

    /// <summary>Raised on the UI thread: (wasInUse, isInUse).</summary>
    public event Action<bool, bool>? InUseChanged;

    public CameraAvailability Availability { get; }

    public bool IsInUse { get; private set; }

    public bool IsMuted => _muted;

    public void SetMuted(bool muted)
    {
        _muted = muted;
        _pipe?.UpdateState(BuildState());
    }

    public IReadOnlyList<CameraInfo> GetCameras()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, VirtualCameraNative.DllName)))
        {
            return [];
        }

        try
        {
            return EnumerateCameras().Where(c => c.Name != VirtualCameraNative.CameraName).ToList();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Logger.Error("Camera enumeration failed", ex);
            return [];
        }
    }

    /// <summary>Creates the virtual camera and starts serving its media source.</summary>
    public async Task StartAsync(string selectedCameraName, string overlayPath, bool powerOffWhenMuted)
    {
        _powerOffWhenMuted = powerOffWhenMuted;
        _overlayPath = overlayPath;
        _overlayVersion++;
        _sourceSymbolicLink = ResolveCamera(selectedCameraName)?.SymbolicLink ?? string.Empty;

        if (Availability != CameraAvailability.Available || _started)
        {
            return;
        }

        _pipe = new CameraPipeServer(BuildState(), BuildOverlay());
        _pipe.InUseChanged += OnPipeInUseChanged;
        _pipe.Start();

        // Media Foundation objects live in the multithreaded apartment; thread-pool threads are MTA.
        string link = _sourceSymbolicLink;
        int hr = await Task.Run(() => VirtualCameraNative.VcmCam_Start(VirtualCameraNative.CameraName, link));
        if (hr < 0)
        {
            Logger.Error($"Creating the virtual camera failed: 0x{hr:X8}");
            return;
        }

        _started = true;
        Logger.Info($"Virtual camera started, source: {(link.Length > 0 ? link : "<first available>")}");
    }

    public void UpdateSourceCamera(string selectedCameraName)
    {
        _sourceSymbolicLink = ResolveCamera(selectedCameraName)?.SymbolicLink ?? string.Empty;
        _pipe?.UpdateState(BuildState());
    }

    public void UpdatePowerOff(bool powerOffWhenMuted)
    {
        _powerOffWhenMuted = powerOffWhenMuted;
        _pipe?.UpdateState(BuildState());
    }

    public void UpdateOverlay(string overlayPath)
    {
        _overlayPath = overlayPath;
        _overlayVersion++;
        _pipe?.UpdateOverlay(BuildOverlay());
        _pipe?.UpdateState(BuildState());
    }

    public void Stop()
    {
        if (_started)
        {
            int hr = Task.Run(VirtualCameraNative.VcmCam_Stop).GetAwaiter().GetResult();
            Logger.Info($"Virtual camera removed (0x{hr:X8})");
            _started = false;
        }

        if (_pipe is not null)
        {
            _pipe.InUseChanged -= OnPipeInUseChanged;
            _pipe.Dispose();
            _pipe = null;
        }
    }

    public void Dispose() => Stop();

    private void OnPipeInUseChanged(bool inUse)
    {
        _dispatcher.BeginInvoke(() =>
        {
            bool was = IsInUse;
            if (was == inUse) return;
            IsInUse = inUse;
            InUseChanged?.Invoke(was, inUse);
        });
    }

    private CameraInfo? ResolveCamera(string name)
    {
        var cameras = GetCameras();
        return cameras.FirstOrDefault(c => c.Name == name) ?? cameras.FirstOrDefault();
    }

    private byte[] BuildState() =>
        CameraPipeProtocol.EncodeState(new(_muted, _powerOffWhenMuted, _overlayVersion, _sourceSymbolicLink));

    private byte[] BuildOverlay()
    {
        byte[] image = [];
        try
        {
            if (!string.IsNullOrEmpty(_overlayPath))
            {
                var info = new FileInfo(_overlayPath);
                if (info.Exists && info.Length <= MaxOverlaySize)
                {
                    image = File.ReadAllBytes(_overlayPath);
                }
                else
                {
                    Logger.Warn($"Overlay image missing or too large: {_overlayPath}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error("Reading the overlay image failed", ex);
        }

        // An empty image makes the camera show black, like the default black.bmp of the original.
        return CameraPipeProtocol.EncodeOverlay(_overlayVersion, image);
    }

    private static CameraAvailability DetectAvailability()
    {
        if (Environment.OSVersion.Version.Build < 22000)
        {
            return CameraAvailability.UnsupportedOs;
        }

        string dll = Path.Combine(AppContext.BaseDirectory, VirtualCameraNative.DllName);
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey($@"SOFTWARE\Classes\CLSID\{VirtualCameraNative.MediaSourceClsid}\InprocServer32");
        if (!File.Exists(dll) || key?.GetValue(null) is not string registered || !File.Exists(registered))
        {
            return CameraAvailability.NotInstalled;
        }

        try
        {
            return VirtualCameraNative.VcmCam_IsSupported() != 0 ? CameraAvailability.Available : CameraAvailability.UnsupportedOs;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Logger.Error("Loading the virtual camera DLL failed", ex);
            return CameraAvailability.NotInstalled;
        }
    }

    private static unsafe List<CameraInfo> EnumerateCameras()
    {
        var list = new List<CameraInfo>();
        var handle = GCHandle.Alloc(list);
        try
        {
            int hr = Task.Run(() => VirtualCameraNative.VcmCam_EnumerateCameras(&OnCamera, GCHandle.ToIntPtr(handle)))
                .GetAwaiter().GetResult();
            if (hr < 0) Logger.Warn($"Camera enumeration returned 0x{hr:X8}");
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static void OnCamera(IntPtr name, IntPtr symbolicLink, IntPtr context)
    {
        var list = (List<CameraInfo>)GCHandle.FromIntPtr(context).Target!;
        list.Add(new CameraInfo(Marshal.PtrToStringUni(name) ?? string.Empty, Marshal.PtrToStringUni(symbolicLink) ?? string.Empty));
    }
}
