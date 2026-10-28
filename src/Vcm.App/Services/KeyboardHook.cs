using System.Runtime.InteropServices;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.Services;

/// <summary>
/// Low-level keyboard hook for shortcuts that another application or Windows already registered, so RegisterHotKey
/// cannot get them. As in the original module the hook sees the keys first and swallows them, which overrides the
/// other owner. It runs on its own thread so a busy UI thread can never make Windows drop the hook for timing out,
/// and it exists only while at least one shortcut needs it.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    // Marks the input we inject ourselves.
    private static readonly IntPtr InjectedMarker = new(0x56434D00);

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly Action<HotkeyAction> _pressed;
    private readonly Action<HotkeyAction> _released;
    private volatile Binding[] _bindings = [];
    private volatile bool _suspended;
    private Thread? _thread;
    private uint _threadId;
    private volatile IntPtr _hook;

    // Main keys currently held down and swallowed, with their action; touched only on the hook thread.
    private readonly Dictionary<int, HotkeyAction> _held = [];

    public KeyboardHook(Action<HotkeyAction> pressed, Action<HotkeyAction> released)
    {
        _proc = HookProc;
        _pressed = pressed;
        _released = released;
    }

    /// <summary>True while the hook is in place; false if there is nothing to hook or Windows refused it.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Replaces the hooked shortcuts; the hook is installed or removed as needed.</summary>
    public void SetBindings(IReadOnlyList<(HotkeyAction Action, HotkeySettings Hotkey)> bindings)
    {
        _bindings = [.. bindings.Select(b => new Binding(b.Action, b.Hotkey))];
        if (bindings.Count > 0) Start();
        else Stop();
    }

    /// <summary>Lets every key through, e.g. while a new shortcut is being recorded.</summary>
    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    private void Start()
    {
        if (_thread is not null) return;

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "VCM keyboard hook" };
        _thread.Start();
        ready.Wait();
    }

    private void Stop()
    {
        if (_thread is null) return;

        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join();
        _thread = null;
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Logger.Error($"Keyboard hook installation failed: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            Logger.Info("Keyboard hook installed for shortcuts used by other applications");
        }
        ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        // Keys still held are no longer swallowed; finish a push-to-talk in progress.
        foreach (var action in _held.Values) _released(action);
        _held.Clear();
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        int msg = wParam.ToInt32();
        int vk = (int)data.vkCode;
        bool down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

        if (data.dwExtraInfo == InjectedMarker)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (!down)
        {
            // The key-down was swallowed, so the key-up must be as well.
            if (_held.Remove(vk, out var action))
            {
                _released(action);
                return 1;
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (_held.ContainsKey(vk))
        {
            return 1; // auto-repeat of a shortcut being held
        }

        if (!_suspended)
        {
            bool win = IsDown(KeyNames.VK_LWIN) || IsDown(KeyNames.VK_RWIN);
            bool ctrl = IsDown(KeyNames.VK_CONTROL);
            bool alt = IsDown(KeyNames.VK_MENU);
            bool shift = IsDown(KeyNames.VK_SHIFT);
            foreach (var binding in _bindings)
            {
                var h = binding.Hotkey;
                if (h.Code == vk && h.Win == win && h.Ctrl == ctrl && h.Alt == alt && h.Shift == shift)
                {
                    _held[vk] = binding.Action;
                    if (win || alt) SuppressMenuActivation();
                    _pressed(binding.Action);
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    // Releasing Win or Alt right after a swallowed key would open the Start menu or activate the menu bar, because
    // Windows saw the modifier pressed alone. An unassigned key in between prevents it, as PowerToys does.
    private static void SuppressMenuActivation()
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = 0xFF, dwFlags = NativeMethods.KEYEVENTF_KEYUP, dwExtraInfo = InjectedMarker },
            },
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose() => Stop();

    private sealed record Binding(HotkeyAction Action, HotkeySettings Hotkey);
}
