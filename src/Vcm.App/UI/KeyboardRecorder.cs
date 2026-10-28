using System.Runtime.InteropServices;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.UI;

/// <summary>
/// Records a key combination while the shortcut dialog is open. A low-level hook is needed so that combinations
/// with the Windows key reach the dialog instead of triggering Windows shortcuts; it exists only while recording.
/// </summary>
internal sealed class KeyboardRecorder : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private bool _win, _ctrl, _alt, _shift;

    public KeyboardRecorder()
    {
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
    }

    /// <summary>A key combination was pressed (modifiers held + one non-modifier key).</summary>
    public event Action<HotkeySettings>? Recorded;

    /// <summary>Only modifiers are held; used to show them while the user is still pressing.</summary>
    public event Action<HotkeySettings>? ModifiersChanged;

    /// <summary>Enter or Esc pressed without modifiers.</summary>
    public event Action<bool>? Committed;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        int msg = wParam.ToInt32();
        bool down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        int vk = (int)data.vkCode;

        switch (vk)
        {
            case KeyNames.VK_LWIN or KeyNames.VK_RWIN:
                _win = down;
                break;
            case KeyNames.VK_CONTROL or KeyNames.VK_LCONTROL or KeyNames.VK_RCONTROL:
                _ctrl = down;
                break;
            case KeyNames.VK_MENU or KeyNames.VK_LMENU or KeyNames.VK_RMENU:
                _alt = down;
                break;
            case KeyNames.VK_SHIFT or KeyNames.VK_LSHIFT or KeyNames.VK_RSHIFT:
                _shift = down;
                break;
            default:
                if (down)
                {
                    bool anyModifier = _win || _ctrl || _alt || _shift;
                    if (!anyModifier && vk == 0x0D) Committed?.Invoke(true);
                    else if (!anyModifier && vk == 0x1B) Committed?.Invoke(false);
                    else if (!anyModifier && vk == KeyNames.VK_TAB) return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                    else Recorded?.Invoke(new HotkeySettings(_win, _ctrl, _alt, _shift, vk));
                }
                return 1;
        }

        if (down)
        {
            ModifiersChanged?.Invoke(new HotkeySettings(_win, _ctrl, _alt, _shift, 0));
        }

        // Swallow the Windows key so the Start menu does not open; let other modifiers through.
        return vk is KeyNames.VK_LWIN or KeyNames.VK_RWIN ? 1 : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
