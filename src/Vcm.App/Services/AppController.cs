using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Vcm.App.Localization;
using Vcm.App.Native;
using Vcm.App.Toolbar;
using Vcm.App.UI;
using Vcm.Core;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Vcm.App.Services;

/// <summary>Wires settings, engine, devices, shortcuts, toolbar and tray together (the role of the PowerToys module).</summary>
internal sealed class AppController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MessageWindow _messageWindow;
    private readonly MicrophoneService _microphones;
    private readonly CameraService _camera;
    private readonly HotkeyService _hotkeys;
    private readonly ToolbarManager _toolbar;
    private readonly TrayIcon _tray;
    private readonly SoundService _sound = new();
    private readonly VcmEngine _engine;
    private VcmSettings _settings;
    private SettingsWindow? _settingsWindow;
    private bool _running;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _settings = SettingsStore.Load();
        _messageWindow = new MessageWindow();
        _messageWindow.AddHandler(OnSystemMessage);

        _microphones = new MicrophoneService(dispatcher);
        _camera = new CameraService(dispatcher);
        _engine = new VcmEngine(_microphones, _camera, () => _settings);
        _hotkeys = new HotkeyService(_messageWindow, dispatcher);
        _toolbar = new ToolbarManager(_engine, () => _settings, dispatcher);
        _tray = new TrayIcon(_messageWindow) { MenuFactory = BuildTrayMenu };

        _engine.StateChanged += OnStateChanged;
        _microphones.MuteReported += _engine.OnMicrophoneMuteReported;
        _microphones.MuteReported += _ => RefreshIndicators();
        _camera.InUseChanged += (was, now) => _engine.OnCameraInUseChanged(was, now);
        _hotkeys.Pressed += OnHotkey;
        _hotkeys.PushToTalkReleased += () => { if (_running) _engine.PushToTalkUp(); };
        _toolbar.Clicked += device =>
        {
            if (device == VcmDevice.Microphone) _engine.ToggleMicrophone();
            else _engine.ToggleCamera();
        };
        _tray.LeftClick += () => { if (_running) _engine.ToggleMicrophone(); };
    }

    public VcmSettings Settings => _settings.Clone();

    public CameraService Camera => _camera;

    public MicrophoneService Microphones => _microphones;

    public HotkeyService Hotkeys => _hotkeys;

    public async Task StartAsync()
    {
        Logger.Info($"VCM starting, settings: enabled={_settings.Enabled}");
        await _camera.StartAsync(_settings.SelectedCamera, _settings.CameraOverlayImagePath, _settings.CameraPowerOffWhenMuted);
        if (_settings.Enabled)
        {
            Enable();
            // As in the original, the startup action is applied when the app (module) starts.
            _engine.ApplyMicrophoneStartupAction();
            _engine.ApplyCameraStartupAction();
        }
        RefreshIndicators();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
    }

    /// <summary>Saves new settings and applies whatever changed, immediately.</summary>
    public void UpdateSettings(VcmSettings updated)
    {
        var old = _settings;
        _settings = updated.Clone();
        SettingsStore.Save(_settings);

        if (old.Enabled != _settings.Enabled)
        {
            if (_settings.Enabled) Enable();
            else Disable();
        }
        else if (_running)
        {
            if (!SameHotkeys(old, _settings)) _hotkeys.Register(_settings);
            if (old.SelectedMicrophone != _settings.SelectedMicrophone)
            {
                _microphones.Select(_settings.SelectedMicrophone);
                _engine.SyncMicrophoneState();
                _engine.ApplyMicrophoneStartupAction();
            }
            if (old.ToolbarPosition != _settings.ToolbarPosition || old.ToolbarMonitor != _settings.ToolbarMonitor) _toolbar.Rebuild();
            else _toolbar.Refresh();
        }

        if (old.SelectedCamera != _settings.SelectedCamera)
        {
            _camera.UpdateSourceCamera(_settings.SelectedCamera);
            if (_running) _engine.ApplyCameraStartupAction();
        }
        if (old.CameraOverlayImagePath != _settings.CameraOverlayImagePath) _camera.UpdateOverlay(_settings.CameraOverlayImagePath);
        if (old.CameraPowerOffWhenMuted != _settings.CameraPowerOffWhenMuted) _camera.UpdatePowerOff(_settings.CameraPowerOffWhenMuted);
        if (old.TrayIconShowsState != _settings.TrayIconShowsState) RefreshIndicators();
    }

    /// <summary>Suspends global shortcuts while the shortcut dialog records keys.</summary>
    public void SuspendHotkeys() => _hotkeys.Suspend();

    public void ResumeHotkeys()
    {
        if (_running) _hotkeys.Resume();
    }

    private void Enable()
    {
        if (_running) return;
        _running = true;
        _microphones.Select(_settings.SelectedMicrophone);
        _engine.SyncMicrophoneState();
        _hotkeys.Register(_settings);
        _toolbar.Show();
        RefreshIndicators();
        Logger.Info("VCM enabled");
    }

    private void Disable()
    {
        if (!_running) return;
        // "Disabling this module or closing VCM will unmute the microphone and camera."
        _engine.UnmuteAll(userInitiated: false);
        _running = false;
        _hotkeys.UnregisterAll();
        _microphones.Release(unmute: true);
        _toolbar.Hide();
        RefreshIndicators();
        Logger.Info("VCM disabled");
    }

    private void OnHotkey(HotkeyAction action)
    {
        if (!_running) return;
        switch (action)
        {
            case HotkeyAction.MuteCameraAndMicrophone: _engine.ToggleAll(); break;
            case HotkeyAction.MuteMicrophone: _engine.ToggleMicrophone(); break;
            case HotkeyAction.PushToTalk: _engine.PushToTalkDown(); break;
            case HotkeyAction.MuteCamera: _engine.ToggleCamera(); break;
        }
    }

    private void OnStateChanged(VcmStateChange change)
    {
        if (change.UserInitiated && _settings.PlaySoundOnToggle)
        {
            _sound.Play(change.Muted);
        }
        if (_running) _toolbar.Flash();
        RefreshIndicators();
    }

    private void RefreshIndicators()
    {
        _toolbar.Refresh();

        bool micMuted = _running && _engine.MicrophoneMuted;
        bool cameraMuted = _running && _engine.CameraEffectivelyMuted;
        const int SM_CXSMICON = 49;
        int size = Math.Max(16, NativeMethods.GetSystemMetricsForDpi(SM_CXSMICON, NativeMethods.GetDpiForWindow(_messageWindow.Handle)));
        IntPtr icon = TrayIconRenderer.Create(_settings.TrayIconShowsState && _running, micMuted, cameraMuted, !_running, size);

        string tooltip = !_running
            ? Loc.Get("TrayPaused")
            : Loc.Format("TrayTooltip",
                Loc.Get(micMuted ? "StateOff" : "StateOn"),
                Loc.Get(!_engine.CameraInUse ? "StateNotInUse" : cameraMuted ? "StateOff" : "StateOn"));
        _tray.Update(icon, tooltip);
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string key, SymbolRegular symbol, Action action, bool isChecked = false, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = Loc.Get(key),
                Icon = new SymbolIcon { Symbol = symbol },
                IsCheckable = isChecked,
                IsChecked = isChecked,
                IsEnabled = enabled,
            };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item("TraySettings", SymbolRegular.Settings24, ShowSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("TrayToggleMicrophone", SymbolRegular.MicOff24, _engine.ToggleMicrophone, _running && _engine.MicrophoneMuted, _running));
        menu.Items.Add(Item("TrayToggleCamera", SymbolRegular.VideoOff24, _engine.ToggleCamera, _running && _engine.CameraMuted, _running && _camera.Availability == CameraAvailability.Available));
        menu.Items.Add(Item("TrayToggleAll", SymbolRegular.CameraOff24, _engine.ToggleAll, enabled: _running));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("TrayPause", SymbolRegular.Pause24, () =>
        {
            var s = Settings;
            s.Enabled = !s.Enabled;
            UpdateSettings(s);
            _settingsWindow?.ReloadFromController();
        }, !_settings.Enabled));
        menu.Items.Add(Item("RunAtStartup", SymbolRegular.Rocket24, () => StartupRegistration.Set(!StartupRegistration.IsEnabled), StartupRegistration.IsEnabled));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("TrayExit", SymbolRegular.Power24, () => Application.Current.Shutdown()));
        return menu;
    }

    private bool OnSystemMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
                _dispatcher.BeginInvoke(_toolbar.Rebuild, DispatcherPriority.Background);
                break;
            case NativeMethods.WM_SETTINGCHANGE:
                const int SPI_SETWORKAREA = 0x002F;
                if (wParam.ToInt32() == SPI_SETWORKAREA)
                {
                    // Taskbar moved or resized.
                    _dispatcher.BeginInvoke(_toolbar.Rebuild, DispatcherPriority.Background);
                }
                else if (lParam != IntPtr.Zero && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                {
                    // Light/dark theme switched.
                    _dispatcher.BeginInvoke(RefreshIndicators, DispatcherPriority.Background);
                }
                break;
        }
        return false;
    }

    private static bool SameHotkeys(VcmSettings a, VcmSettings b) =>
        a.MuteCameraAndMicrophoneHotkey == b.MuteCameraAndMicrophoneHotkey
        && a.MuteMicrophoneHotkey == b.MuteMicrophoneHotkey
        && a.PushToTalkMicrophoneHotkey == b.PushToTalkMicrophoneHotkey
        && a.MuteCameraHotkey == b.MuteCameraHotkey;

    public void Dispose()
    {
        Logger.Info("VCM exiting");
        if (_running)
        {
            _engine.UnmuteAll(userInitiated: false);
        }
        _running = false;
        _settingsWindow?.Close();
        _hotkeys.Dispose();
        _toolbar.Dispose();
        _microphones.Release(unmute: true);
        _microphones.Dispose();
        _camera.Dispose();
        _tray.Dispose();
        _sound.Dispose();
        _messageWindow.Dispose();
    }
}
