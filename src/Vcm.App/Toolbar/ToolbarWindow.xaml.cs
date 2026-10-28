using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Vcm.App.Localization;
using Vcm.App.Native;
using Vcm.Core;
using Wpf.Ui.Controls;

namespace Vcm.App.Toolbar;

/// <summary>One toolbar per monitor: a topmost, non-activating, click-able strip showing mic and camera state.</summary>
public partial class ToolbarWindow : Window
{
    private static readonly Brush DarkBackground = Freeze(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly Brush DarkBorder = Freeze(Color.FromRgb(0x31, 0x31, 0x31));
    private static readonly Brush DarkText = Brushes.White;
    private static readonly Brush DarkDisabled = Freeze(Color.FromRgb(0x58, 0x58, 0x58));
    private static readonly Brush LightBackground = Freeze(Color.FromRgb(0xF1, 0xF1, 0xF1));
    private static readonly Brush LightBorder = Freeze(Color.FromRgb(0xDB, 0xDB, 0xDB));
    private static readonly Brush LightText = Freeze(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush LightDisabled = Freeze(Color.FromRgb(0xC1, 0xC1, 0xC1));

    private readonly IntPtr _monitor;
    private readonly string _position;
    private IntPtr _hwnd;

    internal ToolbarWindow(IntPtr monitor, string position)
    {
        _monitor = monitor;
        _position = position;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Reposition();
    }

    internal event Action<VcmDevice>? Clicked;

    internal void Render(ToolbarVisual visual, bool dark)
    {
        Brush text = dark ? DarkText : LightText;
        Brush disabled = dark ? DarkDisabled : LightDisabled;
        Root.Background = dark ? DarkBackground : LightBackground;
        Root.BorderBrush = dark ? DarkBorder : LightBorder;
        Divider.Fill = dark ? DarkBorder : LightBorder;

        MicIcon.Symbol = visual.MicrophoneOn ? SymbolRegular.Mic24 : SymbolRegular.MicOff24;
        MicIcon.Foreground = text;
        MicLabel.Foreground = text;
        MicLabel.Text = Loc.Get(visual.MicrophoneOn ? "ToolbarMicOn" : "ToolbarMicOff");

        bool cameraUsed = visual.Camera != CameraVisualState.NotInUse;
        CameraIcon.Symbol = visual.Camera == CameraVisualState.On ? SymbolRegular.Video24 : SymbolRegular.VideoOff24;
        CameraIcon.Foreground = cameraUsed ? text : disabled;
        CameraLabel.Foreground = cameraUsed ? text : disabled;
        CameraLabel.Text = Loc.Get(visual.Camera switch
        {
            CameraVisualState.On => "ToolbarCameraOn",
            CameraVisualState.Off => "ToolbarCameraOff",
            _ => "ToolbarCameraNotInUse",
        });
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Clicked?.Invoke(ToolbarLogic.HitTest(e.GetPosition(this).X, ActualWidth));
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        // Keep the toolbar out of screen sharing and recordings.
        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        Reposition();
    }

    /// <summary>Places the toolbar in physical pixels inside the work area of its monitor.</summary>
    internal void Reposition()
    {
        if (_hwnd == IntPtr.Zero || !Monitors.TryGet(_monitor, out var monitor))
        {
            return;
        }

        double scale = monitor.Dpi / 96.0;
        int width = (int)Math.Round(ToolbarLogic.Width * scale);
        int height = (int)Math.Round(ToolbarLogic.Height * scale);
        var position = ToolbarLogic.CalculatePosition(_position, monitor.WorkArea, width, height, scale);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, position.X, position.Y, width, height, NativeMethods.SWP_NOACTIVATE);
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
