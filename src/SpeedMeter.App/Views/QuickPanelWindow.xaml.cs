using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SpeedMeter.App.ViewModels;

namespace SpeedMeter.App.Views;

public partial class QuickPanelWindow : Window
{
    public const double DesignWidthDip = 280.0;
    public const double DesignHeightDip = 300.0;

    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        Deactivated += (_, _) =>
        {
            Hide();
        };
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    /// <summary>
    /// Positions the Quick Panel adjacent to a known source window, deriving its native physical anchor and window DPI.
    /// Uses GetDpiForWindow on the source HWND as the PerMonitorV2 DPI authority.
    /// </summary>
    public PhysicalRect PositionNear(Window sourceWindow, PhysicalRect? workAreaOverride = null)
    {
        if (sourceWindow == null) throw new ArgumentNullException(nameof(sourceWindow));

        uint? sourceDpi = null;
        var sourceHwnd = new WindowInteropHelper(sourceWindow).Handle;
        if (sourceHwnd != 0)
        {
            var dpi = PlacementHelper.GetDpiForWindow(sourceHwnd);
            if (dpi > 0) sourceDpi = dpi;
        }

        if (!PlacementHelper.TryGetWindowPhysicalRect(sourceWindow, out var anchor))
        {
            var dpi = VisualTreeHelper.GetDpi(sourceWindow);
            anchor = new PhysicalRect(
                (int)Math.Round(sourceWindow.Left * dpi.DpiScaleX),
                (int)Math.Round(sourceWindow.Top * dpi.DpiScaleY),
                (int)Math.Round((sourceWindow.ActualWidth > 0 ? sourceWindow.ActualWidth : sourceWindow.Width) * dpi.DpiScaleX),
                (int)Math.Round((sourceWindow.ActualHeight > 0 ? sourceWindow.ActualHeight : sourceWindow.Height) * dpi.DpiScaleY));
        }

        return PositionNearPhysical(anchor, workAreaOverride, sourceDpi);
    }

    /// <summary>
    /// Positions the Quick Panel adjacent to the current mouse cursor (e.g. system tray invocation).
    /// </summary>
    public PhysicalRect PositionNearCursor(PhysicalRect? workAreaOverride = null)
    {
        if (!PlacementHelper.TryGetCursorPhysicalPoint(out var cursor))
        {
            // Documented safe fallback: bottom-right corner of primary work area
            var wa = PlacementHelper.GetMonitorInfoForPoint(new PhysicalPoint(0, 0)).WorkArea;
            cursor = new PhysicalPoint(wa.Right - 60, wa.Bottom - 40);
        }

        var anchor = new PhysicalRect(cursor.X - 2, cursor.Y - 2, 4, 4);
        return PositionNearPhysical(anchor, workAreaOverride);
    }

    /// <summary>
    /// Positions the Quick Panel adjacent to an explicit PhysicalRect anchor.
    /// Resolves target monitor work area, resolves PerMonitorV2 DPI via GetDpiForWindow,
    /// executes physical placement clamping, and positions native HWND via Win32 SetWindowPos.
    /// Native HWND placement is the single authoritative placement operation; does not perform
    /// secondary WPF DIP assignments that corrupt PerMonitorV2 virtual desktop origins.
    /// </summary>
    public PhysicalRect PositionNearPhysical(PhysicalRect physicalAnchor, PhysicalRect? workAreaOverride = null, uint? dpiOverride = null)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        var workArea = workAreaOverride ?? PlacementHelper.GetMonitorInfoForRect(physicalAnchor).WorkArea;

        uint targetDpi = 96;
        if (dpiOverride.HasValue)
        {
            targetDpi = dpiOverride.Value;
        }
        else if (hwnd != 0)
        {
            // Position HWND at anchor coordinates without activating or redrawing so Windows binds it to target monitor
            PlacementHelper.SetWindowPos(
                hwnd, 0,
                physicalAnchor.Left, physicalAnchor.Top, 1, 1,
                PlacementHelper.SWP_NOSIZE | PlacementHelper.SWP_NOZORDER | PlacementHelper.SWP_NOACTIVATE);

            var dpi = PlacementHelper.GetDpiForWindow(hwnd);
            targetDpi = dpi > 0 ? dpi : 96;
        }

        double scale = targetDpi / 96.0;
        int targetWidthPx = (int)Math.Round(DesignWidthDip * scale);
        int targetHeightPx = (int)Math.Round(DesignHeightDip * scale);

        var placed = PlacementHelper.CalculatePlacement(physicalAnchor, targetWidthPx, targetHeightPx, workArea);

        if (hwnd != 0)
        {
            PlacementHelper.SetWindowPos(
                hwnd, 0,
                placed.Left, placed.Top, placed.Width, placed.Height,
                PlacementHelper.SWP_NOZORDER | PlacementHelper.SWP_NOACTIVATE);
        }

        // Single positioning authority: native HWND placement above is authoritative.
        // We do NOT perform post-SetWindowPos WPF Left/Top assignments because dividing
        // global physical virtual desktop origins by local monitor scale is invalid under PerMonitorV2.
        return placed;
    }

    /// <summary>
    /// Legacy overload accepting generic WPF Rect objects for backward compatibility with existing tests.
    /// </summary>
    [Obsolete("Use PositionNearPhysical(PhysicalRect) for PerMonitorV2 coordinate safety.", error: false)]
    public void PositionNear(Rect target, Rect? workAreaOverride = null)
    {
        var physTarget = new PhysicalRect(
            (int)Math.Round(target.Left),
            (int)Math.Round(target.Top),
            (int)Math.Round(target.Width),
            (int)Math.Round(target.Height));

        PhysicalRect? physWa = workAreaOverride.HasValue
            ? new PhysicalRect(
                (int)Math.Round(workAreaOverride.Value.Left),
                (int)Math.Round(workAreaOverride.Value.Top),
                (int)Math.Round(workAreaOverride.Value.Width),
                (int)Math.Round(workAreaOverride.Value.Height))
            : null;

        PositionNearPhysical(physTarget, physWa);
    }
}
