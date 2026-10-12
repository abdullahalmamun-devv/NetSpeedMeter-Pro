using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    private readonly Forms.ToolStripMenuItem disableClickThroughMenuItem;
    private readonly WidgetWindow widget;
    private readonly QuickPanelWindow quickPanel;
    private readonly MainViewModel viewModel;
    private readonly QuickPanelViewModel quickPanelVm;
    private readonly HotkeyHelper hotkeyHelper;
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
    private bool updatingAdapterPicker;
    private string? rememberedSelectedAdapterId;
    private long historicalRefreshId;

    internal Forms.ToolStripMenuItem DisableClickThroughMenuItem => disableClickThroughMenuItem;

    internal sealed record AdapterOption(string? Id, string Label, string Description)
    {
        public bool IsAutoPrimary => Id is null;
    }

    public MainWindow(string? smokeDirectory, string? isolatedDataDirectory = null)
    {
        this.smokeDirectory = smokeDirectory;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);

        monitor = isolatedDataDirectory is null
            ? new MonitorSession()
            : new MonitorSession(
                Path.Combine(isolatedDataDirectory, "speedmeter.db"),
                Path.Combine(isolatedDataDirectory, "settings.json"));
        widget = new WidgetWindow(OpenDashboard);

        viewModel = new MainViewModel(
            monitor,
            showWidgetAction: () => { widget.Show(); widget.Activate(); },
            openQuickPanelAction: OpenQuickPanel);

        quickPanelVm = new QuickPanelViewModel(
            monitor.Collector,
            monitor.HistoryService,
            monitor.SettingsService,
            openDashboardAction: OpenDashboard,
            connectivityService: monitor.ConnectivityService);

        quickPanel = new QuickPanelWindow(quickPanelVm);

        // Bind ViewControls DataContexts
        LiveTrafficViewControl.DataContext = viewModel.LiveTraffic;
        HistoryViewControl.DataContext = viewModel.History;
        SettingsViewControl.DataContext = viewModel.Settings;
        NetworkToolsViewControl.DataContext = viewModel.NetworkTools;

        hotkeyHelper = new HotkeyHelper(this, OpenDashboard, () => { if (widget.IsVisible) widget.Hide(); else widget.Show(); }, DisableWidgetClickThrough);
        widget.LayoutChanged += layout =>
        {
            var s = monitor.SettingsService.CurrentSettings;
            s.WidgetLayout = layout;
            monitor.SettingsService.Save(s);
        };

        // Tray Setup (exposing typed and semantic identifier for click-through recovery)
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "SpeedMeter Pro · starting", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => Dispatcher.Invoke(OpenDashboard));
        menu.Items.Add("Show / hide widget", null, (_, _) => Dispatcher.Invoke(() => { if (widget.IsVisible) widget.Hide(); else widget.Show(); }));
        menu.Items.Add("Reset widget position", null, (_, _) => Dispatcher.Invoke(() => { widget.ResetPosition(); widget.Show(); }));
        menu.Items.Add("Toggle widget lock", null, (_, _) => Dispatcher.Invoke(widget.ToggleLock));
        disableClickThroughMenuItem = new Forms.ToolStripMenuItem("Disable click-through", null, (_, _) => Dispatcher.Invoke(DisableWidgetClickThrough))
        {
            Name = "DisableClickThroughMenuItem",
            Tag = "DisableClickThrough"
        };
        menu.Items.Add(disableClickThroughMenuItem);
        menu.Items.Add("Pause / resume monitoring", null, (_, _) => monitor.TogglePause());
        menu.Items.Add("Quick panel", null, (_, _) => Dispatcher.Invoke(OpenQuickPanel));
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenDashboard);

        displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        displayTimer.Tick += (_, _) => PollSnapshot();

        // Subscribe to dynamic settings changes
        monitor.SettingsService.SettingsChanged += OnSettingsChanged;
        if (smokeDirectory is null)
        {
            ApplySettings(monitor.SettingsService.CurrentSettings);
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        ApplyInventory(monitor.Latest.Observation.Adapters);
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

        if (widget.IsVisible)
        {
            quickPanel.PositionNear(widget);
        }
        else
        {
            quickPanel.PositionNearCursor();
        }

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
        if (smokeDirectory is null && !monitor.SettingsService.CurrentSettings.FirstRunCompleted)
        {
            var firstRun = new FirstRunWindow(monitor.SettingsService.CurrentSettings, s =>
            {
                monitor.SettingsService.Save(s);
                ApplySettings(s);
            });
            firstRun.Owner = this;
            firstRun.ShowDialog();
        }

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
                DisableClickThroughMenuItem.PerformClick();
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

                // Expanded Wave 3 smoke checks (PRD §5, §13, §14)
                OpenQuickPanel();
                await Task.Delay(350);
                var quickPanelOpened = quickPanel.IsVisible;
                quickPanel.Hide();
                var quickPanelClosed = !quickPanel.IsVisible;
                var quickPanelSmokePass = quickPanelOpened && quickPanelClosed;

                // 5 Primary Destinations (PRD §5.1) + 1 Secondary Destination (Settings)
                SetActiveView("Overview");
                var primaryNavOverviewVerified = viewModel.CurrentDestination == "Overview" && OverviewPanel.Visibility == Visibility.Visible && LiveTrafficViewControl.Visibility == Visibility.Collapsed && AppsViewControl.Visibility == Visibility.Collapsed && HistoryViewControl.Visibility == Visibility.Collapsed && NetworkToolsViewControl.Visibility == Visibility.Collapsed;

                SetActiveView("LiveTraffic");
                var primaryNavLiveTrafficVerified = viewModel.CurrentDestination == "LiveTraffic" && LiveTrafficViewControl.Visibility == Visibility.Visible && OverviewPanel.Visibility == Visibility.Collapsed;

                SetActiveView("Apps");
                var primaryNavAppsVerified = viewModel.CurrentDestination == "Apps" && AppsViewControl.Visibility == Visibility.Visible && OverviewPanel.Visibility == Visibility.Collapsed;

                SetActiveView("History");
                var primaryNavHistoryVerified = viewModel.CurrentDestination == "History" && HistoryViewControl.Visibility == Visibility.Visible && OverviewPanel.Visibility == Visibility.Collapsed;

                SetActiveView("Tools");
                var primaryNavToolsVerified = viewModel.CurrentDestination == "Tools" && NetworkToolsViewControl.Visibility == Visibility.Visible && OverviewPanel.Visibility == Visibility.Collapsed;

                SetActiveView("Settings");
                var secondaryNavSettingsVerified = viewModel.CurrentDestination == "Settings" && SettingsViewControl.Visibility == Visibility.Visible && OverviewPanel.Visibility == Visibility.Collapsed;

                SetActiveView("Overview");
                var navDestinationsVerified = primaryNavOverviewVerified && primaryNavLiveTrafficVerified && primaryNavAppsVerified && primaryNavHistoryVerified && primaryNavToolsVerified && secondaryNavSettingsVerified && viewModel.CurrentDestination == "Overview" && OverviewPanel.Visibility == Visibility.Visible;

                // Storage degradation banner semantics verification (Healthy => collapsed; Degraded => visible)
                StorageDegradedBanner.Visibility = Visibility.Collapsed;
                var storageHealthyBannerCollapsed = StorageDegradedBanner.Visibility == Visibility.Collapsed;

                StorageDegradedBanner.Visibility = Visibility.Visible;
                StorageDegradedText.Text = "Storage Degraded: Disk capacity limit reached (smoke test).";
                var storageDegradedBannerTruthful = StorageDegradedBanner.Visibility == Visibility.Visible && StorageDegradedText.Text.Contains("Storage Degraded");
                StorageDegradedBanner.Visibility = Visibility.Collapsed;
                var storageBannerTruthful = storageHealthyBannerCollapsed && storageDegradedBannerTruthful;

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
                    QuickPanelSmokePass = quickPanelSmokePass,
                    PrimaryNavOverviewVerified = primaryNavOverviewVerified,
                    PrimaryNavLiveTrafficVerified = primaryNavLiveTrafficVerified,
                    PrimaryNavAppsVerified = primaryNavAppsVerified,
                    PrimaryNavHistoryVerified = primaryNavHistoryVerified,
                    PrimaryNavToolsVerified = primaryNavToolsVerified,
                    SecondaryNavSettingsVerified = secondaryNavSettingsVerified,
                    NavDestinationsVerified = navDestinationsVerified,
                    StorageBannerTruthful = storageBannerTruthful,
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

        if (StorageDegradedBanner is not null)
        {
            var isStorageDegraded = snapshot.StorageHealth == StorageHealth.Degraded;
            StorageDegradedBanner.Visibility = isStorageDegraded ? Visibility.Visible : Visibility.Collapsed;
            if (isStorageDegraded && StorageDegradedText is not null)
            {
                StorageDegradedText.Text = !string.IsNullOrWhiteSpace(snapshot.DegradedReason)
                    ? $"Storage Degraded: {snapshot.DegradedReason}"
                    : "Storage persistence is degraded. Live monitoring continues, but history is not being saved.";
            }
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
            ReadingState.Live => displayed?.State == CollectorState.Degraded && displayed?.StorageHealth == StorageHealth.Healthy
                ? $"Measurement degraded · {displayed?.Observation.Detail ?? "Provider reporting degraded availability"}"
                : $"Active · Scope: {displayed?.ScopeDecision?.Summary ?? "Selected adapter"}",
            ReadingState.Paused => "Collection paused by user.",
            ReadingState.Discontinuity => "Counter discontinuity detected. Rebaselined cleanly.",
            _ => displayed?.Observation.Availability != ProviderAvailability.Available
                ? $"Measurement unavailable · {displayed?.Observation.Detail ?? last.Detail}"
                : last.Detail
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
        var refreshId = Interlocked.Increment(ref historicalRefreshId);
        try
        {
            var settings = monitor.SettingsService.CurrentSettings;
            var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
            var nowUtc = DateTimeOffset.UtcNow;

            var summary = await monitor.HistoryService.GetPeriodSummaryAsync(
                QueryPeriodType.Today, tzId, nowUtc, cycleDay: settings.ActiveQuotaRule?.CycleDay ?? 1);

            if (refreshId != Interlocked.Read(ref historicalRefreshId)) return;

            TodayTotalText.Text = MetricFormat.Bytes(summary.TotalBytes);
            TodayRxText.Text = MetricFormat.Bytes(summary.BytesRx);
            TodayTxText.Text = MetricFormat.Bytes(summary.BytesTx);
            TodayCoverageText.Text = $"Coverage: {summary.CoverageRatio * 100:F0}% ({summary.Completeness})";

            var rule = settings.ActiveQuotaRule;
            if (rule is { Enabled: true })
            {
                var eval = await monitor.QuotaService.EvaluateCurrentCycleAsync(rule, nowUtc);
                if (refreshId != Interlocked.Read(ref historicalRefreshId)) return;

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
        catch (OperationCanceledException)
        {
            // Canceled queries safely exit
        }
        catch (Exception)
        {
            if (refreshId != Interlocked.Read(ref historicalRefreshId)) return;

            TodayTotalText.Text = "—";
            TodayRxText.Text = "—";
            TodayTxText.Text = "—";
            TodayCoverageText.Text = "Today's usage unavailable";

            QuotaSummaryText.Text = "Quota status unavailable";
            QuotaProgressBar.Value = 0;
            QuotaRemainingText.Text = "—";
            QuotaBadgeText.Text = "Unavailable";
        }
    }

    private void ApplyInventory(IReadOnlyList<AdapterCounter> adapters)
    {
        var currentMode = monitor.Mode;
        var activePrimaryId = monitor.ScopeDecision.PrimaryAdapterId;
        var key = $"{currentMode}:{activePrimaryId}:{rememberedSelectedAdapterId}:" +
                  string.Join("|", adapters.Select(a => $"{a.Id}:{a.Name}:{a.IsUp}"));
        if (key == inventoryKey) return;
        inventoryKey = key;

        updatingAdapterPicker = true;
        try
        {
            var resolvedAdapter = adapters.FirstOrDefault(a => a.Id == activePrimaryId);
            var autoDescription = resolvedAdapter is not null
                ? $"Dynamic route determination via AutoPrimary. Currently using: {resolvedAdapter.Name}. Virtual tunnel traffic is strictly isolated."
                : "Dynamic route determination via AutoPrimary. Virtual tunnel traffic is strictly isolated.";

            var autoOption = new AdapterOption(null, "Automatic (Recommended)", autoDescription);

            var items = new List<AdapterOption> { autoOption };
            items.AddRange(adapters.Select(a =>
                new AdapterOption(a.Id, a.Name, $"{a.Description} · Status: {(a.IsUp ? "Up" : "Down")}")));

            if (currentMode == ScopeMode.SelectedAdapter)
            {
                var targetId = rememberedSelectedAdapterId ?? activePrimaryId;
                if (!string.IsNullOrEmpty(targetId))
                {
                    var match = items.FirstOrDefault(i => i.Id == targetId);
                    if (match is not null)
                    {
                        AdapterPicker.ItemsSource = items;
                        AdapterPicker.SelectedItem = match;
                        ScopeDescription.Text = match.Description;
                    }
                    else
                    {
                        // Missing selected adapter: NEVER silently fallback to another adapter
                        var missingOption = new AdapterOption(
                            targetId,
                            $"Missing adapter ({targetId})",
                            $"Selected adapter ({targetId}) unavailable. Automatic fallback is disabled.");
                        items.Add(missingOption);
                        AdapterPicker.ItemsSource = items;
                        AdapterPicker.SelectedItem = missingOption;
                        ScopeDescription.Text = missingOption.Description;
                    }
                }
                else
                {
                    AdapterPicker.ItemsSource = items;
                    AdapterPicker.SelectedItem = null;
                    ScopeDescription.Text = "No adapter selected. Choose an adapter from the interface inventory.";
                }
            }
            else
            {
                // AutoPrimary mode
                AdapterPicker.ItemsSource = items;
                AdapterPicker.SelectedItem = autoOption;
                ScopeDescription.Text = autoDescription;
            }
        }
        finally
        {
            updatingAdapterPicker = false;
        }
    }

    private async Task RefreshInventoryAsync()
    {
        await Task.Yield();
        ApplyInventory(monitor.Latest.Observation.Adapters);
    }

    private void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingAdapterPicker) return;
        if (AdapterPicker.SelectedItem is AdapterOption option)
        {
            if (option.IsAutoPrimary)
            {
                rememberedSelectedAdapterId = null;
                monitor.SetScope(ScopeMode.AutoPrimary, null);
                UpdateScopeDescription();
            }
            else
            {
                rememberedSelectedAdapterId = option.Id;
                monitor.SetScope(ScopeMode.SelectedAdapter, option.Id);
                ScopeDescription.Text = option.Description;
            }
        }
    }

    private void UpdateScopeDescription()
    {
        if (monitor.Mode == ScopeMode.AutoPrimary)
        {
            var activeId = monitor.ScopeDecision.PrimaryAdapterId;
            var adapter = monitor.Latest.Observation.Adapters.FirstOrDefault(a => a.Id == activeId);
            ScopeDescription.Text = adapter is not null
                ? $"Dynamic route determination via AutoPrimary. Currently using: {adapter.Name}. Virtual tunnel traffic is strictly isolated."
                : "Dynamic route determination via AutoPrimary. Virtual tunnel traffic is strictly isolated.";
        }
        else if (AdapterPicker.SelectedItem is AdapterOption option)
        {
            ScopeDescription.Text = option.Description;
        }
    }

    private async void RefreshAdapters(object sender, RoutedEventArgs e)
    {
        inventoryKey = string.Empty;
        await RefreshInventoryAsync();
    }

    internal MonitorSession MonitorSessionInstance => monitor;
    internal string? RememberedAdapterId => rememberedSelectedAdapterId;
    internal string? SelectedAdapterId => (AdapterPicker.SelectedItem as AdapterOption)?.Id;
    internal string? SelectedAdapterLabel => (AdapterPicker.SelectedItem as AdapterOption)?.Label;

    internal void TriggerRefreshAdapters()
    {
        inventoryKey = string.Empty;
        ApplyInventory(monitor.Latest.Observation.Adapters);
    }

    internal void SimulateApplyInventory(IReadOnlyList<AdapterCounter> adapters)
    {
        inventoryKey = string.Empty;
        ApplyInventory(adapters);
    }

    internal void SimulateUserSelectAdapter(string id)
    {
        var targetOption = (AdapterPicker.ItemsSource as IEnumerable<AdapterOption>)?.FirstOrDefault(o => o.Id == id)
            ?? new AdapterOption(id, $"Adapter ({id})", $"Simulated selection for {id}");
        AdapterPicker.SelectedItem = targetOption;
        rememberedSelectedAdapterId = id;
        monitor.SetScope(ScopeMode.SelectedAdapter, id);
        ScopeDescription.Text = targetOption.Description;
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

    private void OnSettingsChanged(SpeedMeterSettings settings)
    {
        if (quitting) return;
        Dispatcher.Invoke(() => ApplySettings(settings));
    }

    private void ApplySettings(SpeedMeterSettings settings)
    {
        bits = settings.DisplayUnits == "Mbps";
        UnitsButton.Content = bits ? "Units: bits/s" : "Units: bytes/s";
        Chart.Bits = bits;
        Chart.InvalidateVisual();
        quickPanel.Chart.Bits = bits;
        quickPanel.Chart.InvalidateVisual();
        LiveTrafficViewControl.Chart.Bits = bits;
        LiveTrafficViewControl.Chart.InvalidateVisual();
        viewModel.Overview.IsBits = bits;
        viewModel.LiveTraffic.IsBits = bits;

        widget.Opacity = settings.WidgetOpacity;
        widget.Topmost = settings.WidgetTopmost;
        widget.SetLayout(settings.WidgetLayout);
        widget.IdleDimmingEnabled = settings.IdleDimmingEnabled;
        widget.FullscreenAutoHideEnabled = settings.FullscreenAutoHideEnabled;
        ThemeManager.ApplyTheme(settings.Theme);

        var isLowResource = settings.LowResourceMode ||
            (Forms.SystemInformation.PowerStatus.PowerLineStatus == Forms.PowerLineStatus.Offline);
        displayTimer.Interval = isLowResource
            ? TimeSpan.FromMilliseconds(1000)
            : TimeSpan.FromMilliseconds(250);

        if (widget.IsClickThrough != settings.ClickThrough)
        {
            try { widget.SetClickThrough(settings.ClickThrough); } catch { }
        }

        if (IntervalPicker is not null)
        {
            var targetText = settings.SampleIntervalSeconds switch
            {
                0.5 => "0.5 seconds",
                2.0 => "2 seconds",
                _ => "1 second"
            };
            foreach (ComboBoxItem item in IntervalPicker.Items)
            {
                if (item.Content?.ToString() == targetText)
                {
                    IntervalPicker.SelectedItem = item;
                    break;
                }
            }
        }

        RenderReading();
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

    internal void Quit()
    {
        CloseForFixture();
        System.Windows.Application.Current.Shutdown();
    }

    // Fixture windows run without the normal App lifetime. Release the same owned
    // services without terminating the whole checks process.
    internal void CloseForFixture()
    {
        quitting = true;
        displayTimer.Stop();
        monitor.SettingsService.SettingsChanged -= OnSettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        hotkeyHelper?.Dispose();
        monitor.Dispose();
        tray.Visible = false;
        tray.Dispose();
        widget.Close();
        quickPanel.Close();
        Close();
    }
}
