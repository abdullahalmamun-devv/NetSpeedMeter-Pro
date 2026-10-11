using System;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SpeedMeter.App;

public static class PlacementHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Pure, deterministic algorithm for placing a window adjacent to a target anchor within a work area boundary.
    /// </summary>
    public static Rect CalculatePlacement(Rect target, double windowWidth, double windowHeight, Rect workArea)
    {
        // Default position: horizontally centered on target, vertically below target
        double left = target.Left + (target.Width - windowWidth) / 2.0;
        double top = target.Bottom + 6.0;

        // If overflowing bottom of work area, flip above target
        if (top + windowHeight > workArea.Bottom)
        {
            top = target.Top - windowHeight - 6.0;
        }

        // Clamp horizontally within work area
        if (left < workArea.Left + 8.0)
        {
            left = workArea.Left + 8.0;
        }
        if (left + windowWidth > workArea.Right - 8.0)
        {
            left = workArea.Right - windowWidth - 8.0;
        }

        // Clamp vertically within work area
        if (top < workArea.Top + 8.0)
        {
            top = workArea.Top + 8.0;
        }
        if (top + windowHeight > workArea.Bottom - 8.0)
        {
            top = workArea.Bottom - windowHeight - 8.0;
        }

        return new Rect(left, top, windowWidth, windowHeight);
    }

    /// <summary>
    /// Resolves the work area (in WPF DIPs) of the display monitor containing the specified target rectangle.
    /// </summary>
    public static Rect GetWorkAreaForTarget(Rect target)
    {
        try
        {
            var centerX = (int)Math.Round(target.Left + target.Width / 2.0);
            var centerY = (int)Math.Round(target.Top + target.Height / 2.0);

            var pt = new NativePoint { X = centerX, Y = centerY };
            var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

            if (hMon != 0)
            {
                var mi = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    double scaleX = 1.0;
                    double scaleY = 1.0;

                    try
                    {
                        if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
                        {
                            scaleX = dpiX / 96.0;
                            scaleY = dpiY / 96.0;
                        }
                    }
                    catch
                    {
                        // Fallback: 1.0 scaling
                    }

                    return new Rect(
                        mi.rcWork.Left / scaleX,
                        mi.rcWork.Top / scaleY,
                        (mi.rcWork.Right - mi.rcWork.Left) / scaleX,
                        (mi.rcWork.Bottom - mi.rcWork.Top) / scaleY);
                }
            }

            // Fallback: Windows Forms Screen
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
            if (screen is not null)
            {
                var wa = screen.WorkingArea;
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
        }
        catch
        {
            // Fail safe to primary monitor work area
        }

        return SystemParameters.WorkArea;
    }
}
