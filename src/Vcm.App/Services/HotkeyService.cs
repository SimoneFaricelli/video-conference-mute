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
    /// <summary>Used by Windows or another application; taken over through the keyboard hook.</summary>
    Overridden,
    /// <summary>Used by Windows or another application and the keyboard hook is not available.</summary>
    InUseElsewhere,
    /// <summary>Same combination as another VCM shortcut with higher precedence.</summary>
    Duplicate,
    Invalid,
}

/// <summary>
/// Global shortcuts through RegisterHotKey. Unlike the low-level hook of the original module this keeps working
/// over elevated windows and cannot be silently dropped by Windows. Push-to-talk release is detected by polling the
/// key state only while the key is held. Combinations another application already registered go through
/// <see cref="KeyboardHook"/> instead, which overrides them as the original module did.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly DispatcherTimer _releaseTimer;
    private readonly Dictionary<HotkeyAction, HotkeySettings> _hotkeys = [];
    private readonly Dictionary<HotkeyAction, HotkeyStatus> _status = [];
    private readonly KeyboardHook _hook;
    private int _pushToTalkKey;
    private bool _hookPushToTalk;
    private bool _suspended;

    public HotkeyService(MessageWindow window, Dispatcher dispatcher)
    {
        _window = window;
        _window.AddHandler(OnMessage);
        _releaseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input, OnReleaseTimer, dispatcher);
        _releaseTimer.Stop();
        _hook = new KeyboardHook(
            action => dispatcher.BeginInvoke(() => OnHookPressed(action)),
            action => dispatcher.BeginInvoke(() => OnHookReleased(action)));
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
        _hook.SetBindings([]);
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
        _hook.Suspended = true;
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
        var hooked = new List<(HotkeyAction, HotkeySettings)>();
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

            registered.Add(hotkey);
            if (NativeMethods.RegisterHotKey(_window.Handle, (int)action, ToModifiers(hotkey), (uint)hotkey.Code))
            {
                _status[action] = HotkeyStatus.Registered;
            }
            else
            {
                Logger.Warn($"Shortcut {hotkey} for {action} is already in use; overriding it with the keyboard hook");
                hooked.Add((action, hotkey));
            }
        }

        _hook.SetBindings(hooked);
        _hook.Suspended = false;
        foreach (var (action, _) in hooked)
        {
            _status[action] = _hook.IsInstalled ? HotkeyStatus.Overridden : HotkeyStatus.InUseElsewhere;
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
            if (_releaseTimer.IsEnabled || _hookPushToTalk)
            {
                return true;
            }
            _pushToTalkKey = _hotkeys[action].Code;
            _releaseTimer.Start();
        }

        Pressed?.Invoke(action);
        return true;
    }

    private void OnHookPressed(HotkeyAction action)
    {
        if (!_hotkeys.ContainsKey(action) || _status.GetValueOrDefault(action) != HotkeyStatus.Overridden)
        {
            return;
        }

        if (action == HotkeyAction.PushToTalk)
        {
            if (_releaseTimer.IsEnabled || _hookPushToTalk)
            {
                return;
            }
            // The hook swallows the key, so its state cannot be polled: the release comes from the hook.
            _hookPushToTalk = true;
        }

        Pressed?.Invoke(action);
    }

    private void OnHookReleased(HotkeyAction action)
    {
        if (action == HotkeyAction.PushToTalk && _hookPushToTalk)
        {
            _hookPushToTalk = false;
            PushToTalkReleased?.Invoke();
        }
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
        _hookPushToTalk = false;
    }

    public void Dispose()
    {
        UnregisterAll();
        _hook.Dispose();
    }
}
