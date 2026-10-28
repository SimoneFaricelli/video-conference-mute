using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Vcm.App.Native;

namespace Vcm.App.Services;

/// <summary>Notification-area icon built directly on Shell_NotifyIcon (no extra window or dependency).</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_APP + 1;
    private const int NIN_SELECT = 0x0400;
    private const int NIN_KEYSELECT = 0x0401;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private string _tooltip = string.Empty;
    private bool _added;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _window.AddHandler(OnMessage);
    }

    public event Action? LeftClick;

    /// <summary>Builds the context menu each time it opens, so it reflects the current state.</summary>
    public Func<ContextMenu>? MenuFactory { get; set; }

    public void Update(IntPtr icon, string tooltip)
    {
        IntPtr old = _icon;
        _icon = icon;
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        var data = CreateData(NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_MESSAGE | NativeMethods.NIF_SHOWTIP);
        if (!_added)
        {
            _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
            data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
        }
        else
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }

        if (old != IntPtr.Zero) NativeMethods.DestroyIcon(old);
    }

    private NativeMethods.NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: the icon has to be added again.
            _added = false;
            Update(DuplicateCurrentIcon(), _tooltip);
            return false;
        }

        if (msg != CallbackMessage)
        {
            return false;
        }

        switch (lParam.ToInt32() & 0xFFFF)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                LeftClick?.Invoke();
                break;
            case NativeMethods.WM_CONTEXTMENU:
                ShowMenu();
                break;
        }
        return true;
    }

    private IntPtr DuplicateCurrentIcon()
    {
        // Update() destroys the previous handle; hand it a copy-free swap by clearing ours first.
        IntPtr icon = _icon;
        _icon = IntPtr.Zero;
        return icon;
    }

    private void ShowMenu()
    {
        if (MenuFactory is null) return;
        var menu = MenuFactory();
        menu.Placement = PlacementMode.MousePoint;
        // Required so the menu closes when the user clicks elsewhere.
        NativeMethods.SetForegroundWindow(_window.Handle);
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
