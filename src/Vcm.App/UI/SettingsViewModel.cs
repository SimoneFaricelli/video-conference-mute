using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vcm.App.Localization;
using Vcm.App.Services;
using Vcm.Core;

namespace Vcm.App.UI;

public sealed record Option(string Value, string Display);

/// <summary>State of the settings page; every change is saved and applied right away (as in PowerToys).</summary>
internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppController _controller;
    private VcmSettings _settings;
    private IReadOnlyDictionary<HotkeyAction, HotkeyStatus> _hotkeyStatus;
    private string? _previewPath;
    private ImageSource? _preview;

    public SettingsViewModel(AppController controller)
    {
        _controller = controller;
        _settings = controller.Settings;
        _hotkeyStatus = new Dictionary<HotkeyAction, HotkeyStatus>(controller.Hotkeys.Status);
        controller.Hotkeys.StatusChanged += OnHotkeyStatusChanged;
        controller.Microphones.DevicesChanged += RefreshMicrophones;

        ToolbarPositionOptions = Options(ToolbarPositions.All);
        ToolbarMonitorOptions = Options(ToolbarMonitors.Values);
        ToolbarHideOptions = Options(ToolbarHideModes.All);
        StartupActionOptions = Options(StartupActions.All);

        RefreshMicrophones();
        RefreshCameras();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<Option> ToolbarPositionOptions { get; }
    public IReadOnlyList<Option> ToolbarMonitorOptions { get; }
    public IReadOnlyList<Option> ToolbarHideOptions { get; }
    public IReadOnlyList<Option> StartupActionOptions { get; }
    public ObservableCollection<Option> MicrophoneOptions { get; } = [];
    public ObservableCollection<Option> CameraOptions { get; } = [];

    public bool Enabled
    {
        get => _settings.Enabled;
        set => Update(s => s.Enabled = value);
    }

    public HotkeySettings MuteCameraAndMicrophoneHotkey
    {
        get => _settings.MuteCameraAndMicrophoneHotkey;
        set => Update(s => s.MuteCameraAndMicrophoneHotkey = value);
    }

    public HotkeySettings MuteMicrophoneHotkey
    {
        get => _settings.MuteMicrophoneHotkey;
        set => Update(s => s.MuteMicrophoneHotkey = value);
    }

    public HotkeySettings PushToTalkMicrophoneHotkey
    {
        get => _settings.PushToTalkMicrophoneHotkey;
        set => Update(s => s.PushToTalkMicrophoneHotkey = value);
    }

    public HotkeySettings MuteCameraHotkey
    {
        get => _settings.MuteCameraHotkey;
        set => Update(s => s.MuteCameraHotkey = value);
    }

    public string? MuteCameraAndMicrophoneHotkeyWarning => Warning(HotkeyAction.MuteCameraAndMicrophone);
    public string? MuteMicrophoneHotkeyWarning => Warning(HotkeyAction.MuteMicrophone);
    public string? PushToTalkMicrophoneHotkeyWarning => Warning(HotkeyAction.PushToTalk);
    public string? MuteCameraHotkeyWarning => Warning(HotkeyAction.MuteCamera);

    public string SelectedMicrophone
    {
        get => _settings.SelectedMicrophone;
        set
        {
            if (value is not null) Update(s => s.SelectedMicrophone = value);
        }
    }

    public bool PushToReverseEnabled
    {
        get => _settings.PushToReverseEnabled;
        set => Update(s => s.PushToReverseEnabled = value);
    }

    public bool EnforceMicrophoneMute
    {
        get => _settings.EnforceMicrophoneMute;
        set => Update(s => s.EnforceMicrophoneMute = value);
    }

    public bool CameraAvailable => _controller.Camera.Availability == CameraAvailability.Available;

    public bool CameraWarningOpen => !CameraAvailable;

    public string CameraWarning => Loc.Get(_controller.Camera.Availability == CameraAvailability.UnsupportedOs
        ? "CameraUnsupportedOs" : "CameraNotInstalled");

    public bool HasCameras => CameraOptions.Count > 0;

    public string SelectedCamera
    {
        get => _settings.SelectedCamera;
        set
        {
            if (value is not null) Update(s => s.SelectedCamera = value);
        }
    }

    public string CameraOverlayImagePath
    {
        get => _settings.CameraOverlayImagePath;
        set => Update(s => s.CameraOverlayImagePath = value ?? string.Empty);
    }

    public bool HasOverlayImage => !string.IsNullOrEmpty(_settings.CameraOverlayImagePath);

    public ImageSource? OverlayPreview
    {
        get
        {
            string path = _settings.CameraOverlayImagePath;
            if (path != _previewPath)
            {
                _previewPath = path;
                _preview = LoadPreview(path);
            }
            return _preview;
        }
    }

    public bool CameraPowerOffWhenMuted
    {
        get => _settings.CameraPowerOffWhenMuted;
        set => Update(s => s.CameraPowerOffWhenMuted = value);
    }

    public string ToolbarPosition
    {
        get => _settings.ToolbarPosition;
        set
        {
            if (value is not null) Update(s => s.ToolbarPosition = value);
        }
    }

    public string ToolbarMonitor
    {
        get => _settings.ToolbarMonitor;
        set
        {
            if (value is not null) Update(s => s.ToolbarMonitor = value);
        }
    }

    public string ToolbarHide
    {
        get => _settings.ToolbarHide;
        set
        {
            if (value is not null) Update(s => s.ToolbarHide = value);
        }
    }

    public string StartupAction
    {
        get => _settings.StartupAction;
        set
        {
            if (value is not null) Update(s => s.StartupAction = value);
        }
    }

    public bool PlaySoundOnToggle
    {
        get => _settings.PlaySoundOnToggle;
        set => Update(s => s.PlaySoundOnToggle = value);
    }

    public bool TrayIconShowsState
    {
        get => _settings.TrayIconShowsState;
        set => Update(s => s.TrayIconShowsState = value);
    }

    public bool RunAtStartup
    {
        get => StartupRegistration.IsEnabled;
        set
        {
            StartupRegistration.Set(value);
            OnPropertyChanged();
        }
    }

    public void SuspendHotkeys() => _controller.SuspendHotkeys();

    public void ResumeHotkeys() => _controller.ResumeHotkeys();

    public bool IsHotkeyAvailable(HotkeySettings hotkey) => _controller.Hotkeys.IsAvailable(hotkey);

    /// <summary>Re-reads the settings after they were changed elsewhere (tray menu).</summary>
    public void Reload()
    {
        _settings = _controller.Settings;
        OnPropertyChanged(string.Empty);
    }

    public void Detach()
    {
        _controller.Hotkeys.StatusChanged -= OnHotkeyStatusChanged;
        _controller.Microphones.DevicesChanged -= RefreshMicrophones;
    }

    private void RefreshMicrophones()
    {
        string selected = _settings.SelectedMicrophone;
        MicrophoneOptions.Clear();
        MicrophoneOptions.Add(new Option(VcmSettings.AllMicrophones, Loc.Get("AllMicrophones")));
        foreach (string name in _controller.Microphones.GetActiveMicrophoneNames().Distinct())
        {
            MicrophoneOptions.Add(new Option(name, name));
        }
        // Keep a disconnected selection visible instead of silently switching to another device.
        if (MicrophoneOptions.All(o => o.Value != selected))
        {
            MicrophoneOptions.Add(new Option(selected, selected));
        }
        OnPropertyChanged(nameof(SelectedMicrophone));
    }

    private void RefreshCameras()
    {
        CameraOptions.Clear();
        foreach (var camera in _controller.Camera.GetCameras().DistinctBy(c => c.Name))
        {
            CameraOptions.Add(new Option(camera.Name, camera.Name));
        }

        // Like the original page: with no camera chosen yet, the first one is selected and saved.
        if (string.IsNullOrEmpty(_settings.SelectedCamera) && CameraOptions.Count > 0)
        {
            SelectedCamera = CameraOptions[0].Value;
        }
        else if (!string.IsNullOrEmpty(_settings.SelectedCamera) && CameraOptions.All(o => o.Value != _settings.SelectedCamera))
        {
            CameraOptions.Add(new Option(_settings.SelectedCamera, _settings.SelectedCamera));
        }
        OnPropertyChanged(nameof(HasCameras));
        OnPropertyChanged(nameof(SelectedCamera));
    }

    private string? Warning(HotkeyAction action) => _hotkeyStatus.TryGetValue(action, out var status)
        ? status switch
        {
            HotkeyStatus.InUseElsewhere => Loc.Get("HotkeyInUseElsewhere"),
            HotkeyStatus.Duplicate => Loc.Get("HotkeyDuplicate"),
            HotkeyStatus.Invalid => Loc.Get("HotkeyInvalid"),
            _ => null,
        }
        : null;

    private void OnHotkeyStatusChanged(IReadOnlyDictionary<HotkeyAction, HotkeyStatus> status)
    {
        _hotkeyStatus = new Dictionary<HotkeyAction, HotkeyStatus>(status);
        OnPropertyChanged(nameof(MuteCameraAndMicrophoneHotkeyWarning));
        OnPropertyChanged(nameof(MuteMicrophoneHotkeyWarning));
        OnPropertyChanged(nameof(PushToTalkMicrophoneHotkeyWarning));
        OnPropertyChanged(nameof(MuteCameraHotkeyWarning));
    }

    private void Update(Action<VcmSettings> change)
    {
        var updated = _settings.Clone();
        change(updated);
        _settings = updated;
        _controller.UpdateSettings(updated);
        OnPropertyChanged(string.Empty);
    }

    private static ImageSource? LoadPreview(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            image.DecodePixelHeight = 200;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<Option> Options(IEnumerable<string> values) => values.Select(v => new Option(v, Loc.Get(v))).ToList();

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
