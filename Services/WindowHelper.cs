using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using OpenPen.Interop;

namespace OpenPen.Services;

public static class WindowHelper
{
    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    public static void ApplyNoActivate(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(NoActivateHook);
    }

    public static void SetClickThrough(Window window, bool clickThrough)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if (clickThrough)
        {
            ex |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    public static void PlacePhysical(Window window, Rect physicalBounds)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            (int)Math.Round(physicalBounds.X),
            (int)Math.Round(physicalBounds.Y),
            Math.Max(1, (int)Math.Round(physicalBounds.Width)),
            Math.Max(1, (int)Math.Round(physicalBounds.Height)),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public static void PlaceTopLeftKeepSize(Window window, int x, int y)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Keep the current z-order: moving the toolbar must never lift it above
        // a settings/color/text dialog that is currently open.
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public static Rect GetPhysicalBounds(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
    }

    public static void BringToFront(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// OpenPen's overlay and toolbar are deliberately non-activating. That means
    /// Windows may refuse a normal SetForegroundWindow when a dialog needs the
    /// keyboard, so attach to the foreground thread for the hand-off.
    /// </summary>
    public static void ForceForeground(Window window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.SetFocus(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    public static double GetDpiScale(Visual visual)
    {
        try
        {
            return VisualTreeHelper.GetDpi(visual).DpiScaleX;
        }
        catch
        {
            return 1.0;
        }
    }

    private static IntPtr NoActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }
}
