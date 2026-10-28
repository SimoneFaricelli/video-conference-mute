using System.Windows.Interop;
using Vcm.App.Native;

namespace Vcm.App.Services;

/// <summary>
/// Hidden top-level window that receives hotkeys, tray callbacks and display/theme broadcasts. A message-only
/// window would not get broadcasts such as TaskbarCreated, so a never-shown tool window is used instead.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = [];

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("VCM.MessageWindow")
        {
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>Adds a handler returning true when it consumed the message.</summary>
    public void AddHandler(Func<int, IntPtr, IntPtr, bool> handler) => _handlers.Add(handler);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var handler in _handlers)
        {
            if (handler(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
