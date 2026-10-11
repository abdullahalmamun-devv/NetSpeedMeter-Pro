using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpeedMeter.Core;
using SpeedMeter.Application;
using System.Windows.Threading;
using SpeedMeter.Windows;
using SpeedMeter.App.ViewModels;
using SpeedMeter.App.Views;
using Forms = System.Windows.Forms;

namespace SpeedMeter.App;

public partial class MainWindow : Window
{
    private readonly MonitorSession monitor;
    private readonly Forms.NotifyIcon tray;
    private readonly WidgetWindow widget;
    private readonly QuickPanelWindow quickPanel;
    private readonly MainViewModel viewModel;
    private readonly QuickPanelViewModel quickPanelVm;
    private readonly string? smokeDirectory;
    private Reading? last;
    private string inventoryKey = "";
    private bool bits;
    private bool quitting;
    private bool closeExplained;
    private int validReadings;
    private readonly DispatcherTimer displayTimer;
    private CollectorSnapshot? displayed;
    private int tickCount;

    private sealed record AdapterOption(string Id, string Label, string Description);

    public MainWindow(string? smokeDirectory)
    {
        this.smokeDirectory = smokeDirectory;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);

        monitor = new MonitorSession();
        widget = new WidgetWindow(OpenDashboard);

        viewModel = new MainViewModel(
            monitor,
            showWidgetAction: () => { widget.Show(); widget.Activate(); },
            openQuickPanelAction: OpenQuickPanel);

        quickPanelVm = new QuickPanelViewModel(
            monitor.Collector,
            monitor.HistoryService,
            monitor.SettingsService,
            openDashboardAction: OpenDashboard);

        quickPanel = new QuickPanelWindow(quickPanelVm);

        // Bind ViewControls DataContexts
        LiveTrafficViewControl.DataContext = viewModel.LiveTraffic;
        HistoryViewControl.DataContext = viewModel.History;
        SettingsViewControl.DataContext = viewModel.Settings;

        // Tray Setup (Preserving strict index 4 for smoke test)
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "SpeedMeter Pro · starting", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => Dispatcher.Invoke(OpenDashboard));                       // Index 0
        menu.Items.Add("Show / hide widget", null, (_, _) => Dispatcher.Invoke(() => { if (widget.IsVisible) widget.Hide(); else widget.Show(); })); // Index 1
        menu.Items.Add("Reset widget position", null, (_, _) => Dispatcher.Invoke(() => { widget.ResetPosition(); widget.Show(); })); // Index 2
        menu.Items.Add("Toggle widget lock", null, (_, _) => Dispatcher.Invoke(widget.ToggleLock));                // Index 3
        menu.Items.Add("Disable click-through", null, (_, _) => Dispatcher.Invoke(DisableWidgetClickThrough));    // Index 4 (Crucial for smoke test)
        menu.Items.Add("Pause / resume monitoring", null, (_, _) => monitor.TogglePause());                       // Index 5
        menu.Items.Add("Quick panel", null, (_, _) => Dispatcher.Invoke(OpenQuickPanel));                         // Index 6
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));                                           // Index 7
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenDashboard);

        displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        displayTimer.Tick += (_, _) => PollSnapshot();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OpenDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenQuickPanel()
    {
        if (quickPanel.IsVisible)
        {
            quickPanel.Hide();
            return;
        }

        Rect anchor;
        if (widget.IsVisible)
        {
            anchor = new Rect(widget.Left, widget.Top, widget.ActualWidth, widget.ActualHeight);
        }
        else
        {
            // Fallback anchor near bottom right of primary work area
            var wa = SystemParameters.WorkArea;
            anchor = new Rect(wa.Right - 60, wa.Bottom - 40, 50, 30);
        }

        quickPanel.PositionNear(anchor);
        quickPanel.Show();
        quickPanel.Activate();
        _ = quickPanelVm.RefreshTodayUsageAsync();
    }

    private void DisableWidgetClickThrough() => widget.SetClickThrough(false);

    private void ShowWidget(object sender, RoutedEventArgs e)
    {
        widget.Show();
        widget.Activate();
    }

    private void OpenQuickPanelClick(object sender, RoutedEventArgs e) => OpenQuickPanel();

    #region Navigation Routing

    private void SetActiveView(string destination)
    {
        OverviewPanel.Visibility = destination == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        LiveTrafficViewControl.Visibility = destination == "LiveTraffic" ? Visibility.Visible : Visibility.Collapsed;
        AppsViewControl.Visibility = destination == "Apps" ? Visibility.Visible : Visibility.Collapsed;
        HistoryViewControl.Visibility = destination == "History" ? Visibility.Visible : Visibility.Collapsed;
        NetworkToolsViewControl.Visibility = destination == "Tools" ? Visibility.Visible : Visibility.Collapsed;
        SettingsViewControl.Visibility = destination == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        NavOverviewBtn.Background = destination == "Overview" ? (Brush)FindResource("ControlBackgroundBrush") : Brushes.Transparent;
        NavLiveTrafficBtn.Background = destination == "LiveTraffic" ? (Brush)FindResource("ControlBackgroundBrush") : Brushes.Transparent;
        NavAppsBtn.Background = destination == "Apps" ? (Brush)FindResource("ControlBackgroundBrush") : Brushes.Transparent;
        NavHistoryBtn.Background = destination == "History" ? (Brush)FindResource("ControlBackgroundBrush") : Brushes.Transparent;
        NavToolsBtn.Background = destination == "Tools" ? (Brush)FindResource("ControlBackgroundBrush") : Brushes.Transparent;

        viewModel.CurrentDestination = destination;
    }

    private void NavOverviewClick(object sender, RoutedEventArgs e) => SetActiveView("Overview");
    private void NavLiveTrafficClick(object sender, RoutedEventArgs e) => SetActiveView("LiveTraffic");
    private void NavAppsClick(object sender, RoutedEventArgs e) => SetActiveView("Apps");
    private void NavHistoryClick(object sender, RoutedEventArgs e) => SetActiveView("History");
    private void NavToolsClick(object sender, RoutedEventArgs e) => SetActiveView("Tools");
    private void NavSettingsClick(object sender, RoutedEventArgs e) => SetActiveView("Settings");

    #endregion

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshInventoryAsync();
        if (smokeDirectory is not null && AdapterPicker.Items.Count > 0) AdapterPicker.SelectedIndex = 0;
        monitor.Start();
        displayTimer.Start();

        // Initial historical summary refresh
        _ = RefreshHistoricalCardsAsync();

        if (smokeDirectory is not null)
        {
            await Task.Delay(6500);
            try
            {
                Directory.CreateDirectory(smokeDirectory);
                SaveWindowImage(this, Path.Combine(smokeDirectory, "dashboard.png"));
                var liveReading = last;
                var snapshotMatches = last is not null && DownloadValue.Text == MetricFormat.Rate(last.Download, bits);
                ToggleUnits(this, new RoutedEventArgs());
                var unitsMatch = last is not null && bits && DownloadValue.Text == MetricFormat.Rate(last.Download, true) && last.Received == liveReading?.Received;
                monitor.TogglePause();
                await Task.Delay(1500);
                var pausedReading = last;
                await Task.Delay(1500);
                var pausePreservesTotals = last?.State == ReadingState.Paused && last.Received == pausedReading?.Received && last.Download is null;
                widget.SetClickThrough(true);
                var clickThroughBeforeShow = widget.NativeClickThrough && widget.IsClickThrough;
                widget.Show();
                await Task.Delay(250);
                ((Forms.ToolStripMenuItem)tray.ContextMenuStrip!.Items[4]).PerformClick();
                var clickThroughRecoveredViaTray = !widget.NativeClickThrough && !widget.IsClickThrough;
                var widgetPausedTruthful = widget.StatusSummary.StartsWith("Paused");
                if (last is not null)
                {
                    widget.Update(last, bits, "audit", CollectorState.Suspended);
                    var suspendedTruthful = widget.StatusSummary.StartsWith("Suspended");
                    widget.Update(last, bits, "audit", CollectorState.Failed);
                    widgetStatesTruthful = suspendedTruthful && widget.StatusSummary.StartsWith("Failed");
                    widget.Update(last, bits, "audit", CollectorState.Paused);
                }
                SaveWindowImage(widget, Path.Combine(smokeDirectory, "widget.png"));
                widget.Hide();
                monitor.Reset();
                await Task.Delay(250);
                var resetWhilePaused = last?.State == ReadingState.Paused && last.Received == 0 && last.Sent == 0;
                monitor.TogglePause();
                await Task.Delay(3000);
                var resumeIsLive = last?.State == ReadingState.Live;
                File.WriteAllText(Path.Combine(smokeDirectory, "smoke.json"), JsonSerializer.Serialize(new
                {
                    OS = Environment.OSVersion.VersionString,
                    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    LogicalProcessors = Environment.ProcessorCount,
                    ValidReadings = validReadings, LastState = last?.State.ToString(),
                    HasAdapter = AdapterPicker.SelectedItem is not null,
                    Download = liveReading?.Download, Upload = liveReading?.Upload,
                    SnapshotMatches = snapshotMatches, UnitsMatch = unitsMatch,
                    PausePreservesTotals = pausePreservesTotals, ResetWhilePaused = resetWhilePaused, ResumeIsLive = resumeIsLive,
                    ClickThroughBeforeShow = clickThroughBeforeShow, ClickThroughRecoveredViaTray = clickThroughRecoveredViaTray,
                    WidgetPausedTruthful = widgetPausedTruthful, WidgetStatesTruthful = widgetStatesTruthful,
                    WindowWidth = ActualWidth, WindowHeight = ActualHeight
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { Quit(); }
        }
    }

    private bool widgetStatesTruthful;

    private static void SaveWindowImage(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private void PollSnapshot()
    {
        if (quitting) return;
        var snapshot = monitor.Latest;
        if (displayed?.Sequence == snapshot.Sequence) return;
        if (displayed is not null && snapshot.Sequence != displayed.Sequence + 1)
        {
            Chart.Clear();
            quickPanel.Chart.Clear();
            LiveTrafficViewControl.Chart.Clear();
        }

        displayed = snapshot;
        ApplyInventory(snapshot.Observation.Adapters);

        var reading = snapshot.Reading;
        last = reading;

        if (reading.State == ReadingState.Live) validReadings++;

        RenderReading();

        Chart.Add(reading);
        quickPanel.Chart.Add(reading);
        LiveTrafficViewControl.Chart.Add(reading);

        quickPanelVm.UpdateSnapshot(snapshot);
        viewModel.UpdateSnapshot(snapshot);

        // Periodic historical and quota card refresh (~every 3 seconds)
        tickCount++;
        if (tickCount % 12 == 0)
        {
            _ = RefreshHistoricalCardsAsync();
        }
    }

    private void RenderReading()
    {
        if (last is null) return;
        var downFormatted = last.Download.HasValue ? MetricFormat.Rate(last.Download, bits) : "—";
        var upFormatted = last.Upload.HasValue ? MetricFormat.Rate(last.Upload, bits) : "—";

        DownloadValue.Text = last.State == ReadingState.Live ? downFormatted : (last.State == ReadingState.Paused ? "Paused" : "—");
        UploadValue.Text = last.State == ReadingState.Live ? upFormatted : (last.State == ReadingState.Paused ? "Paused" : "—");

        ReceivedValue.Text = $"Session received · {MetricFormat.Bytes(last.Received)}";
        SentValue.Text = $"Session sent · {MetricFormat.Bytes(last.Sent)}";

        StatusText.Text = last.State switch
        {
            ReadingState.Measuring => "Measuring… establishing baseline.",
            ReadingState.Live => $"Active · Scope: {displayed?.ScopeDecision?.Summary ?? "Selected adapter"}",
            ReadingState.Paused => "Collection paused by user.",
            ReadingState.Discontinuity => "Counter discontinuity detected. Rebaselined cleanly.",
            _ => last.Detail
        };

        CoverageText.Text = $"Epoch: {last.MeasurementEpoch} · Monotonic: {last.MonotonicSeconds:F1}s";

        PauseButton.Content = monitor.Paused ? "Resume" : "Pause";
        var scopeName = displayed?.Observation.Adapters.FirstOrDefault(a => a.Id == last.ScopeId)?.Name;
        widget.Update(last, bits, scopeName ?? "Adapter", displayed?.State);

        var text = $"SpeedMeter Pro · ↓ {downFormatted} ↑ {upFormatted}";
        tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private async Task RefreshHistoricalCardsAsync()
    {
        try
        {
            var settings = monitor.SettingsService.CurrentSettings;
            var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
            var nowUtc = DateTimeOffset.UtcNow;

            var summary = await monitor.HistoryService.GetPeriodSummaryAsync(
                QueryPeriodType.Today, tzId, nowUtc, cycleDay: settings.ActiveQuotaRule?.CycleDay ?? 1);

            TodayTotalText.Text = MetricFormat.Bytes(summary.TotalBytes);
            TodayRxText.Text = MetricFormat.Bytes(summary.BytesRx);
            TodayTxText.Text = MetricFormat.Bytes(summary.BytesTx);
            TodayCoverageText.Text = $"Coverage: {summary.CoverageRatio * 100:F0}% ({summary.Completeness})";

            var rule = settings.ActiveQuotaRule;
            if (rule is { Enabled: true })
            {
                var eval = await monitor.QuotaService.EvaluateCurrentCycleAsync(rule, nowUtc);
                var pct = eval.PercentUsed;
                QuotaSummaryText.Text = $"{MetricFormat.Bytes(eval.EffectiveUsedBytes)} / {MetricFormat.Bytes(rule.LimitBytes)} ({pct:F1}%)";
                QuotaProgressBar.Value = Math.Min(100.0, pct);
                QuotaRemainingText.Text = eval.IsExceeded ? $"Over by {MetricFormat.Bytes(eval.OverageBytes)}" : $"Remaining: {MetricFormat.Bytes(eval.RemainingBytes)}";
                QuotaBadgeText.Text = eval.IsExceeded ? "Exceeded" : $"{pct:F0}%";
            }
            else
            {
                QuotaSummaryText.Text = "Quota disabled";
                QuotaProgressBar.Value = 0;
                QuotaRemainingText.Text = "Not configured";
                QuotaBadgeText.Text = "Off";
            }
        }
        catch { }
    }

    private void ApplyInventory(IReadOnlyList<AdapterCounter> adapters)
    {
        var key = string.Join("|", adapters.Select(a => $"{a.Id}:{a.Name}:{a.IsUp}"));
        if (key == inventoryKey) return;
        inventoryKey = key;

        var selected = (AdapterPicker.SelectedItem as AdapterOption)?.Id;
        var items = adapters.Select(a => new AdapterOption(a.Id, a.Name, $"{a.Description} · Status: {(a.IsUp ? "Up" : "Down")}")).ToList();

        AdapterPicker.ItemsSource = items;
        if (selected is not null)
        {
            var match = items.FirstOrDefault(i => i.Id == selected);
            if (match is not null) AdapterPicker.SelectedItem = match;
            else if (items.Count > 0) AdapterPicker.SelectedIndex = 0;
        }
        else if (items.Count > 0 && AdapterPicker.SelectedIndex < 0)
        {
            AdapterPicker.SelectedIndex = 0;
        }
    }

    private async Task RefreshInventoryAsync()
    {
        await Task.Yield();
        ApplyInventory(monitor.Latest.Observation.Adapters);
    }

    private void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterPicker.SelectedItem is AdapterOption option)
        {
            monitor.Select(option.Id);
            ScopeDescription.Text = option.Description;
        }
    }

    private async void RefreshAdapters(object sender, RoutedEventArgs e)
    {
        await RefreshInventoryAsync();
    }

    public void ToggleUnits(object sender, RoutedEventArgs e)
    {
        bits = !bits;
        UnitsButton.Content = bits ? "Units: bits/s" : "Units: bytes/s";
        Chart.Bits = bits;
        Chart.InvalidateVisual();
        quickPanel.Chart.Bits = bits;
        quickPanel.Chart.InvalidateVisual();
        LiveTrafficViewControl.Chart.Bits = bits;
        LiveTrafficViewControl.Chart.InvalidateVisual();
        viewModel.Overview.IsBits = bits;
        viewModel.LiveTraffic.IsBits = bits;
        RenderReading();
    }

    public void TogglePause(object sender, RoutedEventArgs e)
    {
        monitor.TogglePause();
        PauseButton.Content = monitor.Paused ? "Resume" : "Pause";
        PollSnapshot();
    }

    public void ResetSession(object sender, RoutedEventArgs e)
    {
        monitor.Reset();
        Chart.Clear();
        quickPanel.Chart.Clear();
        LiveTrafficViewControl.Chart.Clear();
        PollSnapshot();
    }

    private void IntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalPicker?.SelectedItem is ComboBoxItem item && monitor is not null)
        {
            monitor.Interval = item.Content.ToString() switch
            {
                "0.5 seconds" => 0.5,
                "2 seconds" => 2.0,
                _ => 1.0
            };
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) monitor.SetSuspended(true);
        else if (e.Mode == PowerModes.Resume) monitor.SetSuspended(false);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        widget.EnsureVisibleOnScreen();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (quitting) return;

        // Minimize to tray on close unless quitting
        e.Cancel = true;
        Hide();
        if (!closeExplained)
        {
            closeExplained = true;
            tray.ShowBalloonTip(3000, "SpeedMeter Pro", "SpeedMeter Pro continues monitoring in the background. Open from the tray.", Forms.ToolTipIcon.Info);
        }
    }

    private void QuitClick(object sender, RoutedEventArgs e) => Quit();

    private void Quit()
    {
        quitting = true;
        displayTimer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        tray.Visible = false;
        tray.Dispose();
        widget.Close();
        quickPanel.Close();
        monitor.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
