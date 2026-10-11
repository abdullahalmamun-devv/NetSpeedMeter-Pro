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
using Forms = System.Windows.Forms;

namespace SpeedMeter.App;

public partial class MainWindow : Window
{
    private readonly MonitorSession monitor;
    private readonly Forms.NotifyIcon tray;
    private readonly WidgetWindow widget;
    private readonly string? smokeDirectory;
    private Reading? last;
    private string inventoryKey = "";
    private bool bits;
    private bool quitting;
    private bool closeExplained;
    private int validReadings;
    private readonly DispatcherTimer displayTimer;
    private CollectorSnapshot? displayed;

    private sealed record AdapterOption(string Id, string Label, string Description);

    public MainWindow(string? smokeDirectory)
    {
        this.smokeDirectory = smokeDirectory;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        monitor = new MonitorSession();
        widget = new WidgetWindow(OpenDashboard);
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "SpeedMeter Pro · starting", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => Dispatcher.Invoke(OpenDashboard));
        menu.Items.Add("Show / hide widget", null, (_, _) => Dispatcher.Invoke(() => { if (widget.IsVisible) widget.Hide(); else widget.Show(); }));
        menu.Items.Add("Reset widget position", null, (_, _) => Dispatcher.Invoke(() => { widget.ResetPosition(); widget.Show(); }));
        menu.Items.Add("Toggle widget lock", null, (_, _) => Dispatcher.Invoke(widget.ToggleLock));
        menu.Items.Add("Disable click-through", null, (_, _) => Dispatcher.Invoke(DisableWidgetClickThrough));
        menu.Items.Add("Pause / resume monitoring", null, (_, _) => monitor.TogglePause());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenDashboard);
        displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        displayTimer.Tick += (_, _) => PollSnapshot();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshInventoryAsync();
        if (smokeDirectory is not null && AdapterPicker.Items.Count > 0) AdapterPicker.SelectedIndex = 0;
        monitor.Start();
        displayTimer.Start();
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
    private void DisableWidgetClickThrough() => widget.SetClickThrough(false);

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
        // Display coalescing cannot connect graph points across unseen snapshots.
        if (displayed is not null && snapshot.Sequence != displayed.Sequence + 1) Chart.Clear();
        displayed = snapshot;
        ApplyInventory(snapshot.Observation.Adapters);
        last = snapshot.Reading;
        if (last.IsValid) validReadings++;
        RenderReading();
        Chart.Add(last);
    }

    private void RenderReading()
    {
        if (last is null) return;
        DownloadValue.Text = MetricFormat.Rate(last.Download, bits);
        UploadValue.Text = MetricFormat.Rate(last.Upload, bits);
        ReceivedValue.Text = "Session received · " + MetricFormat.Bytes(last.Received);
        SentValue.Text = "Session sent · " + MetricFormat.Bytes(last.Sent);
        StatusText.Text = last.Detail;
        CoverageText.Text = $"Observed session duration · {TimeSpan.FromSeconds(last.CoveredSeconds):hh\\:mm\\:ss} · gaps excluded · scope changes start a new session";
        PauseButton.Content = monitor.Paused ? "Resume" : "Pause";
        var scopeName = displayed?.Observation.Adapters.FirstOrDefault(a => a.Id == last.ScopeId)?.Name;
        widget.Update(last, bits, scopeName, displayed?.State);
        var text = $"SpeedMeter Pro · ↓ {DownloadValue.Text} · ↑ {UploadValue.Text}";
        tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private async Task RefreshInventoryAsync()
    {
        try { ApplyInventory(await Task.Run(() => new AdapterProvider().Read())); }
        catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
        { StatusText.Text = "Adapter discovery unavailable: " + ex.Message; }
    }

    private void ApplyInventory(IReadOnlyList<AdapterCounter> adapters)
    {
        var key = string.Join("|", adapters.Select(a => a.Id + a.Name + a.IsUp));
        if (key == inventoryKey) return;
        inventoryKey = key;
        var selected = AdapterPicker.SelectedItem as AdapterOption;
        var options = adapters.Select(a => new AdapterOption(a.Id, a.Name + (a.IsUp ? "" : " (disconnected)"), a.Description)).ToList();
        if (selected is not null && options.All(a => a.Id != selected.Id))
            options.Add(selected with { Label = selected.Label + " (unavailable)" });
        AdapterPicker.SelectionChanged -= AdapterChanged;
        AdapterPicker.ItemsSource = options;
        AdapterPicker.SelectedItem = options.FirstOrDefault(a => a.Id == selected?.Id);
        AdapterPicker.SelectionChanged += AdapterChanged;
    }

    private void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (monitor is null) return;
        var selected = AdapterPicker.SelectedItem as AdapterOption;
        monitor.Select(selected?.Id);
        PollSnapshot();
        Chart.Clear();
        ScopeDescription.Text = selected is null ? "Choose an adapter to begin." : selected.Description + " · selected adapter only · LAN traffic included";
    }

    private async void RefreshAdapters(object sender, RoutedEventArgs e) => await RefreshInventoryAsync();
    private void ShowWidget(object sender, RoutedEventArgs e) { widget.Show(); widget.Activate(); }
    private void TogglePause(object sender, RoutedEventArgs e) => monitor.TogglePause();
    private void ResetSession(object sender, RoutedEventArgs e) { monitor.Reset(); Chart.Clear(); }
    private void ToggleUnits(object sender, RoutedEventArgs e)
    {
        bits = !bits; UnitsButton.Content = bits ? "Units: bits/s" : "Units: bytes/s";
        Chart.Bits = bits; Chart.InvalidateVisual(); RenderReading();
    }
    private void IntervalChanged(object sender, SelectionChangedEventArgs e)
    { if (monitor is not null) monitor.Interval = IntervalPicker.SelectedIndex switch { 0 => 0.5, 2 => 2, _ => 1 }; }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) monitor.SetSuspended(true);
        else if (e.Mode == PowerModes.Resume) monitor.SetSuspended(false);
    }
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => widget.ResetPosition());
    private void OpenDashboard() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (quitting) return;
        e.Cancel = true;
        if (!closeExplained)
        {
            MessageBox.Show(this, "Monitoring will continue in the notification area. Use the tray menu to reopen the dashboard or quit.", "SpeedMeter Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            closeExplained = true;
        }
        Hide();
    }
    private void QuitClick(object sender, RoutedEventArgs e) => Quit();
    private void Quit()
    {
        if (quitting) return;
        quitting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        displayTimer.Stop();
        monitor.Dispose();
        tray.Visible = false; tray.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
