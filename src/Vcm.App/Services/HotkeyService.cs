using System.Windows.Threading;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.Services;

internal enum HotkeyAction
{
    MuteCameraAndMicrophone = 1,
    MuteMicrophone = 2,
    PushToTalk = 3,
    MuteCamera = 4,
}

internal enum HotkeyStatus
{
    Registered,
    Disabled,
    /// <summary>Used by Windows or another application.</summary>
    InUseElsewhere,
    /// <summary>Same combination as another VCM shortcut with higher precedence.</summary>
    Duplicate,
    Invalid,
}

/// <summary>
/// Global shortcuts through RegisterHotKey. Unlike the low-level hook of the original module this keeps working
/// over elevated windows, cannot be silently dropped by Windows and reports conflicts. Push-to-talk release is
/// detected by polling the key state only while the key is held.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly DispatcherTimer _releaseTimer;
    private readonly Dictionary<HotkeyAction, HotkeySettings> _hotkeys = [];
    private readonly Dictionary<HotkeyAction, HotkeyStatus> _status = [];
    private int _pushToTalkKey;
    private bool _suspended;

    public HotkeyService(MessageWindow window, Dispatcher dispatcher)
    {
        _window = window;
        _window.AddHandler(OnMessage);
        _releaseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input, OnReleaseTimer, dispatcher);
        _releaseTimer.Stop();
    }

    public event Action<HotkeyAction>? Pressed;

    public event Action? PushToTalkReleased;

    /// <summary>Raised after registration, with the status of every shortcut.</summary>
    public event Action<IReadOnlyDictionary<HotkeyAction, HotkeyStatus>>? StatusChanged;

    public IReadOnlyDictionary<HotkeyAction, HotkeyStatus> Status => _status;

    public void Register(VcmSettings settings)
    {
        _hotkeys.Clear();
        // Order = precedence of the original hook: cam+mic, mic, push-to-talk, camera.
        _hotkeys[HotkeyAction.MuteCameraAndMicrophone] = settings.MuteCameraAndMicrophoneHotkey;
        _hotkeys[HotkeyAction.MuteMicrophone] = settings.MuteMicrophoneHotkey;
        _hotkeys[HotkeyAction.PushToTalk] = settings.PushToTalkMicrophoneHotkey;
        _hotkeys[HotkeyAction.MuteCamera] = settings.MuteCameraHotkey;
        _suspended = false;
        Apply();
    }

    public void UnregisterAll()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            NativeMethods.UnregisterHotKey(_window.Handle, (int)action);
            _status[action] = HotkeyStatus.Disabled;
        }
        StopPushToTalk();
    }

    /// <summary>Temporarily releases the shortcuts, e.g. while a new shortcut is being recorded.</summary>
    public void Suspend()
    {
        _suspended = true;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            NativeMethods.UnregisterHotKey(_window.Handle, (int)action);
        }
    }

    public void Resume()
    {
        if (_suspended)
        {
            _suspended = false;
            Apply();
        }
    }

    /// <summary>True if no other application currently holds this combination.</summary>
    public bool IsAvailable(HotkeySettings hotkey)
    {
        const int probeId = 0x7F00;
        if (!hotkey.IsValid) return false;
        bool ok = NativeMethods.RegisterHotKey(_window.Handle, probeId, ToModifiers(hotkey), (uint)hotkey.Code);
        if (ok) NativeMethods.UnregisterHotKey(_window.Handle, probeId);
        return ok;
    }

    private void Apply()
    {
        var registered = new List<HotkeySettings>();
        foreach (var (action, hotkey) in _hotkeys)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, (int)action);
            if (hotkey.IsEmpty)
            {
                _status[action] = HotkeyStatus.Disabled;
                continue;
            }

            if (!hotkey.IsValid)
            {
                _status[action] = HotkeyStatus.Invalid;
                continue;
            }

            if (registered.Any(r => r.SameCombination(hotkey)))
            {
                _status[action] = HotkeyStatus.Duplicate;
                continue;
            }

            bool ok = NativeMethods.RegisterHotKey(_window.Handle, (int)action, ToModifiers(hotkey), (uint)hotkey.Code);
            _status[action] = ok ? HotkeyStatus.Registered : HotkeyStatus.InUseElsewhere;
            if (ok) registered.Add(hotkey);
            else Logger.Warn($"Shortcut {hotkey} for {action} is already in use");
        }
        StatusChanged?.Invoke(_status);
    }

    private static uint ToModifiers(HotkeySettings h) =>
        NativeMethods.MOD_NOREPEAT
        | (h.Win ? NativeMethods.MOD_WIN : 0)
        | (h.Ctrl ? NativeMethods.MOD_CONTROL : 0)
        | (h.Alt ? NativeMethods.MOD_ALT : 0)
        | (h.Shift ? NativeMethods.MOD_SHIFT : 0);

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return false;
        }

        var action = (HotkeyAction)wParam.ToInt32();
        if (!_hotkeys.ContainsKey(action))
        {
            return false;
        }

        if (action == HotkeyAction.PushToTalk)
        {
            if (_releaseTimer.IsEnabled)
            {
                return true;
            }
            _pushToTalkKey = _hotkeys[action].Code;
            _releaseTimer.Start();
        }

        Pressed?.Invoke(action);
        return true;
    }

    private void OnReleaseTimer(object? sender, EventArgs e)
    {
        // As in the original, only the main key matters for the release; modifiers may be let go first.
        if ((NativeMethods.GetAsyncKeyState(_pushToTalkKey) & 0x8000) == 0)
        {
            StopPushToTalk();
            PushToTalkReleased?.Invoke();
        }
    }

    private void StopPushToTalk()
    {
        _releaseTimer.Stop();
        _pushToTalkKey = 0;
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}
