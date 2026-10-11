using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SpeedMeter.Core;
using SpeedMeter.Application;
using System.ComponentModel;

namespace SpeedMeter.App;

internal sealed class WidgetWindow : Window
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
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    private readonly TextBlock downText;
    private readonly TextBlock upText;
    private readonly TextBlock statusText;
    private readonly MenuItem lockMenuItem;
    private readonly MenuItem clickThroughMenuItem;

    private bool isLocked;
    private bool isClickThrough;

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
    internal string StatusSummary => currentStatusSummary;
    internal bool NativeClickThrough => (GetWindowLongPtr(new WindowInteropHelper(this).EnsureHandle(), GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0;

    private string currentStatusSummary = "Measuring…";

    public WidgetWindow(Action openDashboard)
    {
        Title = "SpeedMeter Pro — Floating Widget";
        Width = 260;
        Height = 136;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 46));
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = true;

        AutomationProperties.SetName(this, "SpeedMeter Pro Floating Widget");
        AutomationProperties.SetHelpText(this, "Displays real-time network download and upload rates. Use arrow keys to reposition.");

        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 83, 108)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10)
        };

        var panel = new StackPanel();

        downText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(121, 185, 255)),
            Text = "↓ —",
            Margin = new Thickness(0, 0, 0, 2)
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(downText, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(downText, "Download Speed: unavailable");

        upText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(114, 219, 200)),
            Text = "↑ —",
            Margin = new Thickness(0, 0, 0, 4)
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(upText, FontNumeralAlignment.Tabular);
        AutomationProperties.SetName(upText, "Upload Speed: unavailable");

        statusText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(168, 182, 202)),
            Text = "Measuring… establishing baseline",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        AutomationProperties.SetName(statusText, "Monitoring Status");

        panel.Children.Add(downText);
        panel.Children.Add(upText);
        panel.Children.Add(statusText);
        container.Child = panel;
        Content = container;

        // Context Menu
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Open Dashboard" };
        openItem.Click += (_, _) => openDashboard();

        lockMenuItem = new MenuItem { Header = "Lock Position", IsCheckable = true, IsChecked = false };
        lockMenuItem.Click += (_, _) => ToggleLock();

        var topmostItem = new MenuItem { Header = "Always on Top", IsCheckable = true, IsChecked = true };
        topmostItem.Click += (_, _) => { Topmost = !Topmost; topmostItem.IsChecked = Topmost; };

        var opacityMenu = new MenuItem { Header = "Opacity" };
        foreach (var (label, val) in new[] { ("100%", 1.0), ("80%", 0.8), ("60%", 0.6) })
        {
            var opItem = new MenuItem { Header = label };
            var targetVal = val;
            opItem.Click += (_, _) => Opacity = targetVal;
            opacityMenu.Items.Add(opItem);
        }

        clickThroughMenuItem = new MenuItem { Header = "Enable Click-through (Recoverable via Tray)", IsCheckable = true, IsChecked = false };
        clickThroughMenuItem.Click += (_, _) => SetClickThrough(!isClickThrough);

        var resetItem = new MenuItem { Header = "Reset Position" };
        resetItem.Click += (_, _) => ResetPosition();

        var hideItem = new MenuItem { Header = "Hide Widget" };
        hideItem.Click += (_, _) => Hide();

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(lockMenuItem);
        menu.Items.Add(topmostItem);
        menu.Items.Add(opacityMenu);
        menu.Items.Add(clickThroughMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(resetItem);
        menu.Items.Add(hideItem);
        ContextMenu = menu;

        // Mouse interactions
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

        // Keyboard navigation (Non-drag positioning & accessibility alternative)
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

    public void ToggleLock()
    {
        IsLocked = !IsLocked;
    }

    public void SetClickThrough(bool enabled)
    {
        // Creating the handle also makes a pre-Show request real, rather than
        // recording a managed flag that was never applied to Windows.
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

    internal void EnsureVisibleOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return;
        // Work areas and HWND rectangles share physical coordinates. A virtual
        // desktop bounding box may contain holes between actual monitors.
        var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - (rect.Right - rect.Left)));
        var y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - (rect.Bottom - rect.Top)));
        if (!SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0015))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to restore the widget position.");
    }

    public void Update(Reading reading, bool bits, string? adapterName, CollectorState? collectorState = null)
    {
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

        // Accessible Narrator description update without spamming announcements
        AutomationProperties.SetName(downText, $"Download: {downStr.TrimStart('↓', ' ')}");
        AutomationProperties.SetName(upText, $"Upload: {upStr.TrimStart('↑', ' ')}");
        AutomationProperties.SetName(statusText, $"Status: {stateDetail}");
    }
}
