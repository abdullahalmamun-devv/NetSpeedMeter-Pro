using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SpeedMeter.App;

/// <summary>
/// Explicit coordinate representations distinguishing Physical Screen Pixels from WPF Device-Independent Pixels.
/// </summary>
public readonly record struct PhysicalPoint(int X, int Y)
{
    public override string ToString() => $"PhysicalPoint({X}, {Y})";
}

public readonly record struct PhysicalRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public static PhysicalRect FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public bool Contains(PhysicalPoint pt) =>
        pt.X >= Left && pt.X < Right && pt.Y >= Top && pt.Y < Bottom;

    public override string ToString() => $"PhysicalRect(L={Left}, T={Top}, W={Width}, H={Height}, R={Right}, B={Bottom})";
}

public readonly record struct DipPoint(double X, double Y)
{
    public override string ToString() => $"DipPoint({X:F1}, {Y:F1})";
}

public readonly record struct DipRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public static DipRect FromLTRB(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));

    public override string ToString() => $"DipRect(L={Left:F1}, T={Top:F1}, W={Width:F1}, H={Height:F1}, R={Right:F1}, B={Bottom:F1})";
}

/// <summary>
/// PerMonitorV2 coordinate-safe placement engine for positioning windows adjacent to targets across multi-monitor layouts.
/// </summary>
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

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Pure, deterministic algorithm for placing a window adjacent to a target anchor in physical pixels.
    /// Clamps strictly within the work area boundary and flips vertically if the window overflows the bottom edge.
    /// </summary>
    public static PhysicalRect CalculatePlacement(
        PhysicalRect target,
        int windowWidthPx,
        int windowHeightPx,
        PhysicalRect workArea)
    {
        const int margin = 8;
        const int verticalSpacing = 6;

        // Default: horizontally centered on target, vertically placed below target
        int left = target.Left + (target.Width - windowWidthPx) / 2;
        int top = target.Bottom + verticalSpacing;

        // If overflowing bottom of work area, flip above target
        if (top + windowHeightPx > workArea.Bottom - margin)
        {
            top = target.Top - windowHeightPx - verticalSpacing;
        }

        // Clamp horizontally within work area
        int minLeft = workArea.Left + margin;
        int maxLeft = workArea.Right - windowWidthPx - margin;
        if (maxLeft < minLeft) maxLeft = minLeft;
        left = Math.Clamp(left, minLeft, maxLeft);

        // Clamp vertically within work area
        int minTop = workArea.Top + margin;
        int maxTop = workArea.Bottom - windowHeightPx - margin;
        if (maxTop < minTop) maxTop = minTop;
        top = Math.Clamp(top, minTop, maxTop);

        return new PhysicalRect(left, top, windowWidthPx, windowHeightPx);
    }

    /// <summary>
    /// Backward-compatible pure placement algorithm operating on explicit DipRect coordinates.
    /// </summary>
    public static DipRect CalculatePlacement(
        DipRect target,
        double windowWidthDip,
        double windowHeightDip,
        DipRect workArea)
    {
        var targetPhys = new PhysicalRect((int)Math.Round(target.Left), (int)Math.Round(target.Top), (int)Math.Round(target.Width), (int)Math.Round(target.Height));
        var waPhys = new PhysicalRect((int)Math.Round(workArea.Left), (int)Math.Round(workArea.Top), (int)Math.Round(workArea.Width), (int)Math.Round(workArea.Height));
        var placed = CalculatePlacement(targetPhys, (int)Math.Round(windowWidthDip), (int)Math.Round(windowHeightDip), waPhys);
        return new DipRect(placed.Left, placed.Top, placed.Width, placed.Height);
    }

    /// <summary>
    /// Legacy overload accepting generic WPF Rect objects for backward compatibility with existing fixtures.
    /// </summary>
    public static Rect CalculatePlacement(Rect target, double windowWidth, double windowHeight, Rect workArea)
    {
        var dipTarget = new DipRect(target.Left, target.Top, target.Width, target.Height);
        var dipWa = new DipRect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        var placed = CalculatePlacement(dipTarget, windowWidth, windowHeight, dipWa);
        return new Rect(placed.Left, placed.Top, placed.Width, placed.Height);
    }

    /// <summary>
    /// Resolves target monitor details (HMonitor, physical work area, physical bounds, DPI) for a physical rectangle.
    /// </summary>
    public static (nint HMonitor, PhysicalRect WorkArea, PhysicalRect MonitorBounds, uint Dpi) GetMonitorInfoForRect(PhysicalRect target)
    {
        var rc = new NativeRect { Left = target.Left, Top = target.Top, Right = target.Right, Bottom = target.Bottom };
        var hMon = MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST);
        return QueryMonitorDetails(hMon);
    }

    /// <summary>
    /// Resolves target monitor details for a physical screen point.
    /// </summary>
    public static (nint HMonitor, PhysicalRect WorkArea, PhysicalRect MonitorBounds, uint Dpi) GetMonitorInfoForPoint(PhysicalPoint pt)
    {
        var p = new NativePoint { X = pt.X, Y = pt.Y };
        var hMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        return QueryMonitorDetails(hMon);
    }

    /// <summary>
    /// Resolves target monitor details for an existing native window handle.
    /// </summary>
    public static (nint HMonitor, PhysicalRect WorkArea, PhysicalRect MonitorBounds, uint Dpi) GetMonitorInfoForWindow(nint hwnd)
    {
        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return QueryMonitorDetails(hMon);
    }

    private static (nint HMonitor, PhysicalRect WorkArea, PhysicalRect MonitorBounds, uint Dpi) QueryMonitorDetails(nint hMon)
    {
        if (hMon != 0)
        {
            var mi = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                uint dpi = 96;
                try
                {
                    if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                    {
                        dpi = dpiX;
                    }
                }
                catch
                {
                    dpi = 96;
                }

                var workArea = PhysicalRect.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
                var bounds = PhysicalRect.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                return (hMon, workArea, bounds, dpi);
            }
        }

        // DOCUMENTED FALLBACK: If Win32 monitor resolution fails completely (e.g. non-Windows host or monitor query error),
        // fallback to primary monitor work area derived from SystemParameters.WorkArea at baseline 96 DPI.
        var wa = SystemParameters.WorkArea;
        var fallbackWa = new PhysicalRect((int)Math.Round(wa.Left), (int)Math.Round(wa.Top), (int)Math.Round(wa.Width), (int)Math.Round(wa.Height));
        return (0, fallbackWa, fallbackWa, 96);
    }

    /// <summary>
    /// Attempts to query the real physical screen rectangle of a WPF window via its native HWND.
    /// </summary>
    public static bool TryGetWindowPhysicalRect(Window window, out PhysicalRect rect)
    {
        if (window != null)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != 0 && GetWindowRect(hwnd, out var nativeRect))
            {
                rect = PhysicalRect.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
                return true;
            }
        }
        rect = default;
        return false;
    }

    /// <summary>
    /// Attempts to query the current cursor position in physical screen pixels.
    /// </summary>
    public static bool TryGetCursorPhysicalPoint(out PhysicalPoint point)
    {
        if (GetCursorPos(out var pt))
        {
            point = new PhysicalPoint(pt.X, pt.Y);
            return true;
        }
        point = default;
        return false;
    }
}
