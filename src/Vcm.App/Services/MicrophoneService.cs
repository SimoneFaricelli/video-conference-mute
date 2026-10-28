using System.Runtime.InteropServices;
using System.Windows.Threading;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.Services;

/// <summary>
/// Controls the mute state of capture endpoints through IAudioEndpointVolume, like the PowerToys MicrophoneDevice.
/// With "[All]" every active microphone is controlled and the default communications device is tracked;
/// otherwise only the microphone with the selected friendly name.
/// </summary>
internal sealed class MicrophoneService : IMicrophoneController, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notificationClient;
    private readonly DispatcherTimer _configurationChangedTimer;
    private readonly List<Microphone> _controlled = [];
    private Microphone? _tracked;
    private string _selection = VcmSettings.AllMicrophones;
    private bool _active;
    private bool _mutedWhileDisconnected;

    public MicrophoneService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _enumerator = CoreAudio.CreateEnumerator();
        _notificationClient = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

        // Device notifications arrive in bursts; handle them once things settle.
        _configurationChangedTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, (_, _) =>
        {
            _configurationChangedTimer!.Stop();
            OnConfigurationChanged();
        }, dispatcher);
        _configurationChangedTimer.Stop();
    }

    /// <summary>Raised on the UI thread when the tracked microphone reports a mute change (from any application).</summary>
    public event Action<bool>? MuteReported;

    /// <summary>Raised on the UI thread when microphones are added or removed.</summary>
    public event Action? DevicesChanged;

    public bool IsMuted => _tracked?.TryGetMute() ?? _mutedWhileDisconnected;

    public bool HasControlledDevices => _controlled.Count > 0;

    public IReadOnlyList<string> GetActiveMicrophoneNames()
    {
        var names = new List<string>();
        foreach (var device in EnumerateActive())
        {
            names.Add(CoreAudio.GetFriendlyName(device));
            Marshal.ReleaseComObject(device);
        }
        return names;
    }

    /// <summary>Starts controlling the selected microphones. Previously controlled ones are unmuted first.</summary>
    public void Select(string selection)
    {
        Release(unmute: true);
        _selection = string.IsNullOrEmpty(selection) ? VcmSettings.AllMicrophones : selection;
        _active = true;
        Bind();
    }

    /// <summary>Stops controlling microphones, optionally unmuting them.</summary>
    public void Release(bool unmute)
    {
        _active = false;
        foreach (var mic in _controlled)
        {
            if (unmute) mic.TrySetMute(false);
            mic.Dispose();
        }
        _controlled.Clear();
        _tracked = null;
        if (unmute) _mutedWhileDisconnected = false;
    }

    public void SetMuted(bool muted)
    {
        // Applied to every controlled device so they stay in sync even if one was changed elsewhere.
        foreach (var mic in _controlled)
        {
            mic.TrySetMute(muted);
        }
        _mutedWhileDisconnected = muted;
    }

    private void Bind()
    {
        bool all = _selection == VcmSettings.AllMicrophones;
        string? defaultId = all ? GetDefaultCommunicationsId() : null;

        foreach (var device in EnumerateActive())
        {
            string name = CoreAudio.GetFriendlyName(device);
            if (!all && name != _selection)
            {
                Marshal.ReleaseComObject(device);
                continue;
            }

            var mic = Microphone.TryCreate(device, name);
            if (mic is null)
            {
                continue;
            }

            _controlled.Add(mic);
            if (!all)
            {
                break;
            }
        }

        _tracked = all
            ? _controlled.FirstOrDefault(m => m.Id == defaultId) ?? _controlled.FirstOrDefault()
            : _controlled.FirstOrDefault();
        _tracked?.StartTracking(OnTrackedMuteNotification);
        Logger.Info($"Microphones controlled: {string.Join(", ", _controlled.Select(m => m.Name))}; tracked: {_tracked?.Name ?? "none"}");
    }

    /// <summary>
    /// Hot-plug handling. New microphones take the current state; removed ones are dropped; the tracked one follows
    /// the default communications device. A disconnected selected microphone is re-bound when it comes back.
    /// </summary>
    private void OnConfigurationChanged()
    {
        DevicesChanged?.Invoke();
        if (!_active)
        {
            return;
        }

        bool muted = IsMuted;
        var activeIds = new HashSet<string>();
        var newMics = new List<Microphone>();
        bool all = _selection == VcmSettings.AllMicrophones;

        foreach (var device in EnumerateActive())
        {
            string id = CoreAudio.GetId(device);
            activeIds.Add(id);
            if (_controlled.Any(m => m.Id == id))
            {
                Marshal.ReleaseComObject(device);
                continue;
            }

            string name = CoreAudio.GetFriendlyName(device);
            bool wanted = all || (name == _selection && _controlled.Count == 0 && newMics.Count == 0);
            var mic = wanted ? Microphone.TryCreate(device, name) : null;
            if (mic is null)
            {
                Marshal.ReleaseComObject(device);
                continue;
            }
            newMics.Add(mic);
        }

        foreach (var gone in _controlled.Where(m => !activeIds.Contains(m.Id)).ToList())
        {
            Logger.Info($"Microphone removed: {gone.Name}");
            _controlled.Remove(gone);
            if (gone == _tracked) _tracked = null;
            gone.Dispose();
        }

        foreach (var mic in newMics)
        {
            Logger.Info($"Microphone added: {mic.Name}");
            mic.TrySetMute(muted);
            _controlled.Add(mic);
        }

        string? defaultId = all ? GetDefaultCommunicationsId() : null;
        var tracked = all
            ? _controlled.FirstOrDefault(m => m.Id == defaultId) ?? _controlled.FirstOrDefault()
            : _controlled.FirstOrDefault();
        if (tracked != _tracked)
        {
            _tracked?.StopTracking();
            _tracked = tracked;
            _tracked?.StartTracking(OnTrackedMuteNotification);
        }

        _mutedWhileDisconnected = muted;
        MuteReported?.Invoke(IsMuted);
    }

    private void OnTrackedMuteNotification(bool muted)
    {
        _dispatcher.BeginInvoke(() => MuteReported?.Invoke(muted));
    }

    private void ScheduleConfigurationChanged()
    {
        _dispatcher.BeginInvoke(() =>
        {
            _configurationChangedTimer.Stop();
            _configurationChangedTimer.Start();
        });
    }

    private IEnumerable<IMMDevice> EnumerateActive()
    {
        if (_enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DeviceState.Active, out var collection) != 0)
        {
            yield break;
        }

        try
        {
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) == 0)
                {
                    yield return device;
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    private string? GetDefaultCommunicationsId()
    {
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var device) != 0)
        {
            return null;
        }

        try
        {
            return CoreAudio.GetId(device);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public void Dispose()
    {
        _configurationChangedTimer.Stop();
        Release(unmute: false);
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        Marshal.ReleaseComObject(_enumerator);
    }

    private sealed class Microphone : IDisposable
    {
        private readonly IMMDevice _device;
        private readonly IAudioEndpointVolume _volume;
        private VolumeCallback? _callback;

        private Microphone(IMMDevice device, IAudioEndpointVolume volume, string id, string name)
        {
            _device = device;
            _volume = volume;
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }

        public static Microphone? TryCreate(IMMDevice device, string name)
        {
            var iid = CoreAudio.IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out object instance) != 0)
            {
                Marshal.ReleaseComObject(device);
                return null;
            }
            return new Microphone(device, (IAudioEndpointVolume)instance, CoreAudio.GetId(device), name);
        }

        public bool? TryGetMute() => _volume.GetMute(out bool muted) == 0 ? muted : null;

        public void TrySetMute(bool muted)
        {
            var context = CoreAudio.VcmEventContext;
            int hr = _volume.SetMute(muted, ref context);
            if (hr < 0) Logger.Warn($"SetMute({muted}) failed on {Name}: 0x{hr:X8}");
        }

        public void StartTracking(Action<bool> onMuteChanged)
        {
            StopTracking();
            _callback = new VolumeCallback(onMuteChanged);
            _volume.RegisterControlChangeNotify(_callback);
        }

        public void StopTracking()
        {
            if (_callback is not null)
            {
                _volume.UnregisterControlChangeNotify(_callback);
                _callback = null;
            }
        }

        public void Dispose()
        {
            StopTracking();
            Marshal.ReleaseComObject(_volume);
            Marshal.ReleaseComObject(_device);
        }
    }

    [ComVisible(true)]
    private sealed class VolumeCallback(Action<bool> onMuteChanged) : IAudioEndpointVolumeCallback
    {
        private bool? _lastMuted;

        public int OnNotify(IntPtr notifyData)
        {
            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
            bool muted = data.bMuted != 0;
            // Volume-only changes also notify; only mute transitions are interesting.
            if (_lastMuted != muted)
            {
                _lastMuted = muted;
                onMuteChanged(muted);
            }
            return 0;
        }
    }

    [ComVisible(true)]
    private sealed class NotificationClient(MicrophoneService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => owner.ScheduleConfigurationChanged();
        public void OnDeviceAdded(string deviceId) => owner.ScheduleConfigurationChanged();
        public void OnDeviceRemoved(string deviceId) => owner.ScheduleConfigurationChanged();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow != EDataFlow.eRender && role == ERole.eCommunications)
            {
                owner.ScheduleConfigurationChanged();
            }
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            if (key.fmtid == PropertyKey.DeviceFriendlyName.fmtid && key.pid == PropertyKey.DeviceFriendlyName.pid)
            {
                owner.ScheduleConfigurationChanged();
            }
        }
    }
}
