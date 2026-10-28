using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vcm.App.Native;
using Wpf.Ui.Controls;

namespace Vcm.App.Services;

/// <summary>Draws tray icons from Fluent System Icons glyphs and turns them into HICONs.</summary>
internal static class TrayIconRenderer
{
    private static readonly Color MutedColor = Color.FromRgb(0xE8, 0x11, 0x23);

    public static IntPtr Create(bool showState, bool micMuted, bool cameraMuted, bool paused, int size)
    {
        bool dark = SystemTheme.TaskbarUsesDarkTheme;
        var foreground = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        var muted = new SolidColorBrush(MutedColor);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (!showState)
            {
                DrawGlyph(dc, SymbolRegular.Video24, foreground, size, size * 0.95, new Point(size / 2.0, size / 2.0));
            }
            else
            {
                var mic = micMuted ? SymbolRegular.MicOff24 : SymbolRegular.Mic24;
                DrawGlyph(dc, mic, micMuted ? muted : foreground, size, size, new Point(size / 2.0, size / 2.0), filled: micMuted);
                if (cameraMuted)
                {
                    // Badge in the bottom-right corner.
                    double r = size * 0.27;
                    var center = new Point(size - r, size - r);
                    dc.DrawEllipse(muted, null, center, r, r);
                    DrawGlyph(dc, SymbolRegular.VideoOff24, Brushes.White, size, r * 1.5, center, filled: true);
                }
            }

            if (paused)
            {
                double r = size * 0.24;
                var center = new Point(size - r, r);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), null, center, r, r);
                DrawGlyph(dc, SymbolRegular.Pause24, Brushes.White, size, r * 1.4, center, filled: true);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return CreateIcon(pixels, size);
    }

    private static void DrawGlyph(DrawingContext dc, SymbolRegular symbol, Brush brush, int size, double emSize, Point center, bool filled = false)
    {
        string fontKey = filled ? "FluentSystemIconsFilled" : "FluentSystemIcons";
        var family = Application.Current.TryFindResource(fontKey) as FontFamily ?? new FontFamily("Segoe Fluent Icons");
        var text = new FormattedText(((char)(int)symbol).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), emSize, brush, 1.0);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static IntPtr CreateIcon(byte[] premultipliedBgra, int size)
    {
        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        IntPtr color = NativeMethods.CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        // Icons expect straight (non-premultiplied) alpha.
        for (int i = 0; i < premultipliedBgra.Length; i += 4)
        {
            byte a = premultipliedBgra[i + 3];
            if (a is > 0 and < 255)
            {
                premultipliedBgra[i] = (byte)Math.Min(255, premultipliedBgra[i] * 255 / a);
                premultipliedBgra[i + 1] = (byte)Math.Min(255, premultipliedBgra[i + 1] * 255 / a);
                premultipliedBgra[i + 2] = (byte)Math.Min(255, premultipliedBgra[i + 2] * 255 / a);
            }
        }
        Marshal.Copy(premultipliedBgra, 0, bits, premultipliedBgra.Length);

        IntPtr mask = NativeMethods.CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        var info = new NativeMethods.ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        IntPtr icon = NativeMethods.CreateIconIndirect(ref info);
        NativeMethods.DeleteObject(color);
        NativeMethods.DeleteObject(mask);
        return icon;
    }
}
