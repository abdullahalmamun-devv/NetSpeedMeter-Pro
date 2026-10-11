using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App;

public sealed class WidgetWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    private readonly TextBlock downText;
    private readonly TextBlock upText;
    private readonly TextBlock statusText;
    private readonly TextBlock detailsText;
    private readonly TrafficChart chart;
    private readonly Border container;
    private readonly MenuItem lockMenuItem;
    private readonly MenuItem clickThroughMenuItem;
    private readonly MenuItem layoutMinimalMenuItem;
    private readonly MenuItem layoutCompactMenuItem;
    private readonly MenuItem layoutDetailedMenuItem;

    private bool isLocked;
    private bool isClickThrough;
    private string currentLayout = "Minimal";
    private double baseOpacity = 0.90;
    private bool isDimmed;
    private long idleStartUtcMs;
    private bool hiddenByFullscreen;

    public bool IsLocked
    {
        get => isLocked;
        set
        {
            isLocked = value;
            lockMenuItem.IsChecked = value;
            statusText.Text = (isLocked ? "Locked · " : "") + currentStatusSummary;
        }
    }

    public bool IsClickThrough => isClickThrough;
    public string CurrentLayout => currentLayout;
    public bool IdleDimmingEnabled { get; set; } = false;
    public bool FullscreenAutoHideEnabled { get; set; } = true;

    internal string StatusSummary => currentStatusSummary;
    internal bool NativeClickThrough => (GetWindowLongPtr(new WindowInteropHelper(this).EnsureHandle(), GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0;

    private string currentStatusSummary = "Measuring…";

    public event Action<string>? LayoutChanged;

    public WidgetWindow(Action openDashboard)
    {
        Title = "SpeedMeter Pro — Floating Widget";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 46));
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = true;

        AutomationProperties.SetName(this, "SpeedMeter Pro Floating Widget");
        AutomationProperties.SetHelpText(this, "Displays real-time network download and upload rates. Use arrow keys to reposition.");

        container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 83, 108)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var panel = new StackPanel();

        var ratesPanel = new StackPanel { Orientation = Orientation.Horizontal };

        downText = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(121, 185, 255)),
            Text = "↓ —",
            Margin = new Thickness(0, 0, 12, 0)
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(downText, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(downText, "Download Speed: unavailable");

        upText = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(114, 219, 200)),
            Text = "↑ —",
            Margin = new Thickness(0, 0, 0, 0)
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(upText, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(upText, "Upload Speed: unavailable");

        ratesPanel.Children.Add(downText);
        ratesPanel.Children.Add(upText);

        chart = new TrafficChart
        {
            Height = 36,
            Margin = new Thickness(0, 4, 0, 4),
            Visibility = Visibility.Collapsed
        };

        detailsText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Text = "",
            Margin = new Thickness(0, 2, 0, 2),
            Visibility = Visibility.Collapsed
        };

        statusText = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(168, 182, 202)),
            Text = "Measuring… establishing baseline",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        AutomationProperties.SetName(statusText, "Monitoring Status");

        panel.Children.Add(ratesPanel);
        panel.Children.Add(chart);
        panel.Children.Add(detailsText);
        panel.Children.Add(statusText);
        container.Child = panel;
        Content = container;

        // Context Menu
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Open Dashboard" };
        openItem.Click += (_, _) => openDashboard();

        // Layout Submenu
        var layoutMenu = new MenuItem { Header = "Layout" };
        layoutMinimalMenuItem = new MenuItem { Header = "Minimal", IsCheckable = true, IsChecked = true };
        layoutMinimalMenuItem.Click += (_, _) => SetLayout("Minimal");
        layoutCompactMenuItem = new MenuItem { Header = "Compact Graph", IsCheckable = true, IsChecked = false };
        layoutCompactMenuItem.Click += (_, _) => SetLayout("CompactGraph");
        layoutDetailedMenuItem = new MenuItem { Header = "Detailed", IsCheckable = true, IsChecked = false };
        layoutDetailedMenuItem.Click += (_, _) => SetLayout("Detailed");

        layoutMenu.Items.Add(layoutMinimalMenuItem);
        layoutMenu.Items.Add(layoutCompactMenuItem);
        layoutMenu.Items.Add(layoutDetailedMenuItem);

        lockMenuItem = new MenuItem { Header = "Lock Position", IsCheckable = true, IsChecked = false };
        lockMenuItem.Click += (_, _) => ToggleLock();

        var topmostItem = new MenuItem { Header = "Always on Top", IsCheckable = true, IsChecked = true };
        topmostItem.Click += (_, _) => { Topmost = !Topmost; topmostItem.IsChecked = Topmost; };

        var opacityMenu = new MenuItem { Header = "Opacity" };
        foreach (var (label, val) in new[] { ("100%", 1.0), ("80%", 0.8), ("60%", 0.6) })
        {
            var opItem = new MenuItem { Header = label };
            var targetVal = val;
            opItem.Click += (_, _) => { baseOpacity = targetVal; Opacity = targetVal; isDimmed = false; };
            opacityMenu.Items.Add(opItem);
        }

        clickThroughMenuItem = new MenuItem { Header = "Enable Click-through (Recoverable via Tray / Ctrl+Shift+C)", IsCheckable = true, IsChecked = false };
        clickThroughMenuItem.Click += (_, _) => SetClickThrough(!isClickThrough);

        var resetItem = new MenuItem { Header = "Reset Position" };
        resetItem.Click += (_, _) => ResetPosition();

        var hideItem = new MenuItem { Header = "Hide Widget" };
        hideItem.Click += (_, _) => Hide();

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(layoutMenu);
        menu.Items.Add(lockMenuItem);
        menu.Items.Add(topmostItem);
        menu.Items.Add(opacityMenu);
        menu.Items.Add(clickThroughMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetItem);
        menu.Items.Add(hideItem);
        ContextMenu = menu;

        // Apply initial layout
        ApplyLayoutDimensions("Minimal");

        // Mouse interactions
        MouseEnter += (_, _) =>
        {
            if (isDimmed)
            {
                Opacity = baseOpacity;
                isDimmed = false;
            }
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                openDashboard();
            }
            else if (!isLocked)
            {
                DragMove();
                EnsureVisibleOnScreen();
            }
        };

        // Keyboard navigation
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            else if (e.Key == Key.Enter || e.Key == Key.Space) { openDashboard(); e.Handled = true; }
            else if (e.Key == Key.L) { ToggleLock(); e.Handled = true; }
            else if (!isLocked)
            {
                const double nudge = 12.0;
                switch (e.Key)
                {
                    case Key.Left: Left -= nudge; EnsureVisibleOnScreen(); e.Handled = true; break;
                    case Key.Right: Left += nudge; EnsureVisibleOnScreen(); e.Handled = true; break;
                    case Key.Up: Top -= nudge; EnsureVisibleOnScreen(); e.Handled = true; break;
                    case Key.Down: Top += nudge; EnsureVisibleOnScreen(); e.Handled = true; break;
                }
            }
        };

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        ResetPosition();
        Loaded += (_, _) => EnsureVisibleOnScreen();
    }

    public void SetLayout(string layout)
    {
        if (currentLayout == layout) return;
        currentLayout = layout;
        ApplyLayoutDimensions(layout);
        layoutMinimalMenuItem.IsChecked = layout == "Minimal";
        layoutCompactMenuItem.IsChecked = layout == "CompactGraph";
        layoutDetailedMenuItem.IsChecked = layout == "Detailed";
        EnsureVisibleOnScreen();
        LayoutChanged?.Invoke(layout);
    }

    private void ApplyLayoutDimensions(string layout)
    {
        switch (layout)
        {
            case "CompactGraph":
                Width = 240;
                Height = 110;
                chart.Visibility = Visibility.Visible;
                detailsText.Visibility = Visibility.Collapsed;
                statusText.Visibility = Visibility.Visible;
                downText.FontSize = 16;
                upText.FontSize = 16;
                break;

            case "Detailed":
                Width = 280;
                Height = 145;
                chart.Visibility = Visibility.Visible;
                detailsText.Visibility = Visibility.Visible;
                statusText.Visibility = Visibility.Visible;
                downText.FontSize = 18;
                upText.FontSize = 18;
                break;

            case "Minimal":
            default:
                Width = 200;
                Height = 72;
                chart.Visibility = Visibility.Collapsed;
                detailsText.Visibility = Visibility.Collapsed;
                statusText.Visibility = Visibility.Visible;
                downText.FontSize = 16;
                upText.FontSize = 16;
                break;
        }
    }

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
    }

    public void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE);
        Marshal.SetLastPInvokeError(0);
        if (enabled)
        {
            SetWindowLongPtr(handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        else
        {
            SetWindowLongPtr(handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        }
        var error = Marshal.GetLastPInvokeError();
        if (error != 0 || NativeClickThrough != enabled)
            throw new Win32Exception(error, "Unable to change widget click-through. Use the tray to reopen the dashboard.");
        isClickThrough = enabled;
        clickThroughMenuItem.IsChecked = enabled;
    }

    public void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 24);
        Top = Math.Max(area.Top, area.Bottom - Height - 24);
        EnsureVisibleOnScreen();
    }

    public void EnsureVisibleOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return;

        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMon == 0) return;

        var mi = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(hMon, ref mi)) return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = Math.Clamp(rect.Left, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - width));
        var y = Math.Clamp(rect.Top, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - height));
        if (!SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0015))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to restore the widget position.");
    }

    public void Update(Reading reading, bool bits, string? adapterName, CollectorState? collectorState = null)
    {
        chart.Bits = bits;
        chart.Add(reading);

        string downStr;
        string upStr;
        string stateDetail;

        switch (reading.State)
        {
            case ReadingState.Live:
                downStr = "↓ " + MetricFormat.Rate(reading.Download, bits);
                upStr = "↑ " + MetricFormat.Rate(reading.Upload, bits);
                var isIdle = (reading.Download ?? 0) == 0 && (reading.Upload ?? 0) == 0;
                stateDetail = isIdle
                    ? $"Live · Idle · {adapterName ?? "Connected"}"
                    : $"Live · {adapterName ?? "Connected"}";

                // Idle dimming logic
                var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (isIdle)
                {
                    if (idleStartUtcMs == 0) idleStartUtcMs = nowUtcMs;
                    if (IdleDimmingEnabled && nowUtcMs - idleStartUtcMs >= 30_000 && !isDimmed)
                    {
                        Opacity = Math.Max(0.20, baseOpacity * 0.60);
                        isDimmed = true;
                    }
                }
                else
                {
                    idleStartUtcMs = 0;
                    if (isDimmed)
                    {
                        Opacity = baseOpacity;
                        isDimmed = false;
                    }
                }
                break;

            case ReadingState.Measuring:
                downStr = "↓ —";
                upStr = "↑ —";
                stateDetail = "Measuring… establishing baseline";
                break;

            case ReadingState.Paused:
                downStr = "↓ —";
                upStr = "↑ —";
                stateDetail = "Paused · unobserved gap";
                break;

            case ReadingState.Discontinuity:
                downStr = "↓ —";
                upStr = "↑ —";
                stateDetail = "Rebaselining · counter reset";
                break;

            case ReadingState.Unavailable:
            default:
                downStr = "↓ —";
                upStr = "↑ —";
                stateDetail = string.IsNullOrEmpty(adapterName)
                    ? "Unavailable · select an adapter"
                    : $"Unavailable · {reading.Detail}";
                break;
        }

        if (collectorState is CollectorState.Suspended or CollectorState.Failed or CollectorState.Degraded or CollectorState.Stopped)
        {
            downStr = "↓ —";
            upStr = "↑ —";
            stateDetail = collectorState + " · " + reading.Detail;
        }

        downText.Text = downStr;
        upText.Text = upStr;
        currentStatusSummary = stateDetail;
        statusText.Text = (isLocked ? "Locked · " : "") + stateDetail;
        statusText.ToolTip = reading.Detail;

        if (currentLayout == "Detailed")
        {
            detailsText.Text = adapterName is not null ? $"Adapter: {adapterName}" : "Adapter: Auto";
        }

        // Accessible Narrator description update without spamming announcements
        AutomationProperties.SetName(downText, $"Download: {downStr.TrimStart('↓', ' ')}");
        AutomationProperties.SetName(upText, $"Upload: {upStr.TrimStart('↑', ' ')}");
        AutomationProperties.SetName(statusText, $"Status: {stateDetail}");

        // Fullscreen Auto-Hide check
        CheckFullscreenAutoHide();
    }

    private void CheckFullscreenAutoHide()
    {
        if (!FullscreenAutoHideEnabled)
        {
            if (hiddenByFullscreen)
            {
                Show();
                hiddenByFullscreen = false;
            }
            return;
        }

        try
        {
            var fg = GetForegroundWindow();
            var myHwnd = new WindowInteropHelper(this).Handle;
            if (fg == 0 || fg == myHwnd || fg == GetShellWindow() || fg == GetDesktopWindow())
            {
                if (hiddenByFullscreen)
                {
                    Show();
                    hiddenByFullscreen = false;
                }
                return;
            }

            var hMon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (hMon == 0) return;

            var mi = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
            if (!GetMonitorInfo(hMon, ref mi)) return;

            if (GetWindowRect(fg, out var fgRect))
            {
                var isFs = fgRect.Left <= mi.rcMonitor.Left &&
                           fgRect.Top <= mi.rcMonitor.Top &&
                           fgRect.Right >= mi.rcMonitor.Right &&
                           fgRect.Bottom >= mi.rcMonitor.Bottom;

                if (isFs && !hiddenByFullscreen && IsVisible)
                {
                    Hide();
                    hiddenByFullscreen = true;
                }
                else if (!isFs && hiddenByFullscreen)
                {
                    Show();
                    hiddenByFullscreen = false;
                }
            }
        }
        catch { }
    }
}
