using System;
using System.Runtime.InteropServices;

namespace SpeedMeter.Windows;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
    Unknown = -1
}

public readonly record struct TaskbarRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public sealed record TaskbarInfo(
    TaskbarRect Bounds,
    TaskbarEdge Edge,
    bool IsAutoHide,
    bool IsValid);

public readonly record struct DockedPlacement(
    double X,
    double Y,
    bool UseFloatingFallback,
    string Reason);

public static class TaskbarCompanion
{
    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint ABS_AUTOHIDE = 0x00000001;

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    public static uint GetTaskbarCreatedMessageId()
    {
        return OperatingSystem.IsWindows() ? RegisterWindowMessage("TaskbarCreated") : 0;
    }

    public static TaskbarInfo QueryTaskbar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new TaskbarInfo(default, TaskbarEdge.Unknown, false, false);
        }

        try
        {
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            var data = new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
                hWnd = trayWnd
            };
            var posResult = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
            if (posResult != nint.Zero)
            {
                var edge = data.uEdge switch
                {
                    0 => TaskbarEdge.Left,
                    1 => TaskbarEdge.Top,
                    2 => TaskbarEdge.Right,
                    3 => TaskbarEdge.Bottom,
                    _ => TaskbarEdge.Unknown
                };

                var stateData = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = trayWnd };
                var stateResult = (uint)SHAppBarMessage(ABM_GETSTATE, ref stateData);
                var isAutoHide = (stateResult & ABS_AUTOHIDE) != 0;

                var bounds = new TaskbarRect(data.rc.Left, data.rc.Top, data.rc.Right, data.rc.Bottom);
                return new TaskbarInfo(bounds, edge, isAutoHide, true);
            }

            if (trayWnd != nint.Zero && GetWindowRect(trayWnd, out var rect))
            {
                var edge = TaskbarEdge.Bottom;
                if (rect.Top == 0 && rect.Bottom < 200) edge = TaskbarEdge.Top;
                else if (rect.Left == 0 && rect.Right < 200) edge = TaskbarEdge.Left;
                else if (rect.Right > 1000 && rect.Left > 800) edge = TaskbarEdge.Right;
                return new TaskbarInfo(new TaskbarRect(rect.Left, rect.Top, rect.Right, rect.Bottom), edge, false, true);
            }

            return new TaskbarInfo(default, TaskbarEdge.Unknown, false, false);
        }
        catch
        {
            return new TaskbarInfo(default, TaskbarEdge.Unknown, false, false);
        }
    }

    public static DockedPlacement CalculatePlacement(
        TaskbarInfo taskbar,
        double companionWidth,
        double companionHeight,
        TaskbarRect workArea)
    {
        if (!taskbar.IsValid)
        {
            // Fallback: place in bottom-right of work area
            return new DockedPlacement(
                Math.Max(workArea.Left, workArea.Right - companionWidth - 16),
                Math.Max(workArea.Top, workArea.Bottom - companionHeight - 16),
                true,
                "Taskbar geometry unavailable. Using safe floating fallback.");
        }

        if (taskbar.IsAutoHide)
        {
            // INVARIANT: When auto-hide is enabled, a docked window prevents taskbar access or flickers. Fall back to floating.
            return new DockedPlacement(
                Math.Max(workArea.Left, workArea.Right - companionWidth - 24),
                Math.Max(workArea.Top, workArea.Bottom - companionHeight - 48),
                true,
                "Taskbar auto-hide is active. Floating fallback prevents shell obstruction.");
        }

        // Calculate adjacent dock position based on edge
        switch (taskbar.Edge)
        {
            case TaskbarEdge.Bottom:
            {
                // Adjacent right edge near system tray, directly above taskbar top
                var x = taskbar.Bounds.Right - companionWidth - 12;
                var y = taskbar.Bounds.Top - companionHeight - 4;
                return new DockedPlacement(x, y, false, "Docked adjacent to bottom taskbar.");
            }

            case TaskbarEdge.Top:
            {
                var x = taskbar.Bounds.Right - companionWidth - 12;
                var y = taskbar.Bounds.Bottom + 4;
                return new DockedPlacement(x, y, false, "Docked adjacent to top taskbar.");
            }

            case TaskbarEdge.Left:
            {
                var x = taskbar.Bounds.Right + 4;
                var y = taskbar.Bounds.Bottom - companionHeight - 12;
                return new DockedPlacement(x, y, false, "Docked adjacent to left taskbar.");
            }

            case TaskbarEdge.Right:
            {
                var x = taskbar.Bounds.Left - companionWidth - 4;
                var y = taskbar.Bounds.Bottom - companionHeight - 12;
                return new DockedPlacement(x, y, false, "Docked adjacent to right taskbar.");
            }

            default:
                return new DockedPlacement(
                    workArea.Right - companionWidth - 16,
                    workArea.Bottom - companionHeight - 16,
                    true,
                    "Unknown taskbar edge. Floating fallback used.");
        }
    }
}
