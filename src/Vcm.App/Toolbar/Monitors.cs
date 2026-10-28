using System.Runtime.InteropServices;
using Vcm.App.Native;
using Vcm.Core;

namespace Vcm.App.Toolbar;

internal readonly record struct MonitorInfo(IntPtr Handle, PixelRect WorkArea, bool IsPrimary, uint Dpi);

internal static class Monitors
{
    public static List<MonitorInfo> GetAll()
    {
        var result = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (TryGet(handle, out var info)) result.Add(info);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool TryGet(IntPtr handle, out MonitorInfo info)
    {
        var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(handle, ref mi))
        {
            info = default;
            return false;
        }

        uint dpi = NativeMethods.GetDpiForMonitor(handle, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 ? dpiX : 96;
        var work = new PixelRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
        info = new MonitorInfo(handle, work, (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0, dpi);
        return true;
    }
}
