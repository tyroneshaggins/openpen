using System.Windows;
using OpenPen.Interop;

namespace OpenPen.Services;

public sealed record MonitorInfo(Rect Bounds, Rect WorkArea, bool IsPrimary, string DeviceName)
{
    public Point Center => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
}

public static class MonitorService
{
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorInfo(
                    new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height),
                    new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    info.szDevice));
            }

            return true;
        }

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }
        catch
        {
            // fall through to the WinForms fallback
        }

        if (monitors.Count == 0)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                monitors.Add(new MonitorInfo(
                    new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                    new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height),
                    screen.Primary,
                    screen.DeviceName));
            }
        }

        return monitors;
    }

    public static Rect GetVirtualBounds()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return new Rect(0, 0, 1920, 1080);
        }

        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    public static MonitorInfo GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault()
            ?? new MonitorInfo(new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), true, string.Empty);
    }

    public static Point GetCursorPosition()
    {
        return NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(0, 0);
    }

    public static MonitorInfo? GetMonitorAt(Point physicalPoint)
    {
        var point = new NativeMethods.POINT { X = (int)Math.Round(physicalPoint.X), Y = (int)Math.Round(physicalPoint.Y) };
        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty
        };

        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return null;
        }

        return new MonitorInfo(
            new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height),
            new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height),
            (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
            info.szDevice);
    }

    public static double GetDpiScaleAt(Point physicalPoint)
    {
        var point = new NativeMethods.POINT { X = (int)Math.Round(physicalPoint.X), Y = (int)Math.Round(physicalPoint.Y) };
        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero)
        {
            return 1.0;
        }

        try
        {
            if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // ignore
        }

        return 1.0;
    }
}
