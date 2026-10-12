using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Windows;

namespace SpeedMeter.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;
    private readonly IUsageHistoryService? historyService;
    private readonly IConfigExportImportService? configService;
    private readonly IUpdateService updateService;
    private CancellationTokenSource? updateCancellation;

    private string theme = "Dark";
    private bool bits;
    private double interval = 1.0;
    private string selectedTimezoneId = TimeZoneInfo.Local.Id;
    private int billingCycleDay = 1;
    private double widgetOpacity = 0.90;
    private bool widgetTopmost = true;
    private bool widgetClickThrough = false;
    private string widgetLayout = "Minimal";
    private bool idleDimmingEnabled;
    private bool fullscreenAutoHideEnabled = true;
    private bool connectionChecksEnabled;
    private bool meteredProbesAllowed;
    private bool lowResourceMode;
    private bool startWithWindows;
    private int dataRetentionDays = 0;
    private string? statusMessage;
    private bool autoUpdateCheckEnabled;
    private UpdateStatus updateStatus = new(UpdateState.Idle, "Updates have not been checked.");

    public string Theme { get => theme; set => SetProperty(ref theme, value); }
    public bool Bits { get => bits; set => SetProperty(ref bits, value); }
    public double Interval { get => interval; set => SetProperty(ref interval, value); }
    public string SelectedTimezoneId { get => selectedTimezoneId; set => SetProperty(ref selectedTimezoneId, value); }
    public int BillingCycleDay { get => billingCycleDay; set => SetProperty(ref billingCycleDay, Math.Clamp(value, 1, 31)); }
    public double WidgetOpacity { get => widgetOpacity; set => SetProperty(ref widgetOpacity, Math.Clamp(value, 0.2, 1.0)); }
    public bool WidgetTopmost { get => widgetTopmost; set => SetProperty(ref widgetTopmost, value); }
    public bool WidgetClickThrough { get => widgetClickThrough; set => SetProperty(ref widgetClickThrough, value); }
    public string WidgetLayout { get => widgetLayout; set => SetProperty(ref widgetLayout, value); }
    public bool IdleDimmingEnabled { get => idleDimmingEnabled; set => SetProperty(ref idleDimmingEnabled, value); }
    public bool FullscreenAutoHideEnabled { get => fullscreenAutoHideEnabled; set => SetProperty(ref fullscreenAutoHideEnabled, value); }
    public bool ConnectionChecksEnabled { get => connectionChecksEnabled; set => SetProperty(ref connectionChecksEnabled, value); }
    public bool MeteredProbesAllowed { get => meteredProbesAllowed; set => SetProperty(ref meteredProbesAllowed, value); }
    public bool LowResourceMode { get => lowResourceMode; set => SetProperty(ref lowResourceMode, value); }
    public bool StartWithWindows { get => startWithWindows; set => SetProperty(ref startWithWindows, value); }
    public int DataRetentionDays { get => dataRetentionDays; set => SetProperty(ref dataRetentionDays, value); }
    public string? StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public bool AutoUpdateCheckEnabled { get => autoUpdateCheckEnabled; set => SetProperty(ref autoUpdateCheckEnabled, value); }
    public UpdateStatus UpdateStatus { get => updateStatus; private set => SetProperty(ref updateStatus, value); }
    public string CurrentVersion => ProductIdentity.ProductVersion;
    public string UpdateChannel => ProductIdentity.ReleaseChannel == "rc" ? "Release Candidate" : "Stable";
    public bool UpdateAvailable => UpdateStatus.State is UpdateState.Available or UpdateState.Downloading or UpdateState.Verifying or UpdateState.ReadyToInstall;
    public bool CanUpdateNow => UpdateStatus.State is UpdateState.Available or UpdateState.ReadyToInstall;
    public bool UpdateBusy => UpdateStatus.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Verifying;
    public double UpdateProgress => UpdateStatus.TotalBytes > 0 ? 100.0 * UpdateStatus.DownloadedBytes / UpdateStatus.TotalBytes : 0;
    public string UpdateSize => UpdateStatus.Manifest is { } m ? $"Download size: {m.PackageSizeBytes / 1024d / 1024d:0.0} MB" : "";

    public ObservableCollection<string> AvailableTimezones { get; } = [];

    public ICommand SaveCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand PruneHistoryCommand { get; }
    public ICommand ClearAllHistoryCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand UpdateNowCommand { get; }
    public ICommand LaterCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand CancelUpdateCommand { get; }

    public SettingsViewModel(
        ISettingsService settingsService,
        IUsageHistoryService? historyService = null,
        IConfigExportImportService? configService = null,
        IUpdateService? updateService = null)
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.historyService = historyService;
        this.configService = configService;
        this.updateService = updateService ?? new UpdateService(settingsService);
        this.updateService.StatusChanged += OnUpdateStatusChanged;
        settingsService.SettingsChanged += OnSettingsChangedForUpdates;

        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            AvailableTimezones.Add(tz.Id);
        }

        LoadCurrent();

        SaveCommand = new RelayCommand(Save);
        ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
        PruneHistoryCommand = new RelayCommand(async () => await PruneHistoryAsync());
        ClearAllHistoryCommand = new RelayCommand(async () => await ClearAllHistoryAsync());
        ExportConfigCommand = new RelayCommand(ExportConfig);
        ImportConfigCommand = new RelayCommand(ImportConfig);
        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
        UpdateNowCommand = new RelayCommand(async () => await UpdateNowAsync());
        LaterCommand = new RelayCommand(() => StatusMessage = "Update deferred. You can install it later from Settings.");
        OpenReleaseNotesCommand = new RelayCommand(OpenReleaseNotes);
        CancelUpdateCommand = new RelayCommand(() => updateCancellation?.Cancel());

        // The service itself enforces consent and cadence before any network operation.
        _ = CheckAutomaticallyAsync();
    }

    private void OnSettingsChangedForUpdates(SpeedMeterSettings changed)
    {
        if (changed.AutoUpdateCheckEnabled && updateService.Status.State is not
            (UpdateState.Checking or UpdateState.Downloading or UpdateState.Verifying))
            _ = CheckAutomaticallyAsync();
    }

    private async Task CheckAutomaticallyAsync()
    {
        try { await updateService.CheckAsync(manual: false); }
        catch { /* Update service reports consumer-safe state. */ }
    }

    private void OnUpdateStatusChanged(UpdateStatus next)
    {
        void Apply()
        {
            UpdateStatus = next;
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(CanUpdateNow));
            OnPropertyChanged(nameof(UpdateBusy));
            OnPropertyChanged(nameof(UpdateProgress));
            OnPropertyChanged(nameof(UpdateSize));
        }
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke((Action)Apply);
        else Apply();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (UpdateBusy) return;
        updateCancellation?.Dispose();
        updateCancellation = new CancellationTokenSource();
        await updateService.CheckAsync(manual: true, updateCancellation.Token);
    }

    private async Task UpdateNowAsync()
    {
        if (UpdateBusy || !UpdateAvailable) return;
        updateCancellation?.Dispose();
        updateCancellation = new CancellationTokenSource();
        var result = updateService.Status.State == UpdateState.ReadyToInstall
            ? updateService.Status
            : await updateService.DownloadAndVerifyAsync(updateCancellation.Token);
        if (result.State == UpdateState.ReadyToInstall)
        {
            try { updateService.InstallVerifiedUpdate(); }
            catch { StatusMessage = "Unable to launch the verified installer. Your current installation is unchanged."; }
        }
    }

    private void OpenReleaseNotes()
    {
        if (updateService.Status.Manifest is not { } manifest) return;
        try { Process.Start(new ProcessStartInfo(manifest.ReleaseNotesUrl) { UseShellExecute = true }); }
        catch { StatusMessage = "Unable to open release notes in your browser."; }
    }

    public void LoadCurrent()
    {
        var s = settingsService.CurrentSettings;
        theme = s.Theme;
        bits = s.DisplayUnits == "Mbps";
        interval = s.SampleIntervalSeconds;
        selectedTimezoneId = s.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        billingCycleDay = s.ActiveQuotaRule?.CycleDay ?? 1;
        widgetOpacity = s.WidgetOpacity;
        widgetTopmost = s.WidgetTopmost;
        widgetClickThrough = s.ClickThrough;
        widgetLayout = s.WidgetLayout;
        idleDimmingEnabled = s.IdleDimmingEnabled;
        fullscreenAutoHideEnabled = s.FullscreenAutoHideEnabled;
        connectionChecksEnabled = s.ConnectionChecksEnabled;
        meteredProbesAllowed = s.MeteredProbesAllowed;
        lowResourceMode = s.LowResourceMode;
        dataRetentionDays = s.DataRetentionDays;
        startWithWindows = StartupHelper.IsStartupEnabled();
        autoUpdateCheckEnabled = s.AutoUpdateCheckEnabled;

        OnPropertyChanged(string.Empty);
    }

    public void Save()
    {
        try
        {
            var s = settingsService.CurrentSettings;
            s.Theme = theme;
            s.DisplayUnits = bits ? "Mbps" : "MB/s";
            s.SampleIntervalSeconds = interval;
            s.ReportingTimezoneId = selectedTimezoneId;
            s.WidgetOpacity = widgetOpacity;
            s.WidgetTopmost = widgetTopmost;
            s.ClickThrough = widgetClickThrough;
            s.WidgetLayout = widgetLayout;
            s.IdleDimmingEnabled = idleDimmingEnabled;
            s.FullscreenAutoHideEnabled = fullscreenAutoHideEnabled;
            s.ConnectionChecksEnabled = connectionChecksEnabled;
            s.MeteredProbesAllowed = meteredProbesAllowed;
            s.LowResourceMode = lowResourceMode;
            s.DataRetentionDays = dataRetentionDays;
            s.StartWithWindows = startWithWindows;
            s.AutoUpdateCheckEnabled = autoUpdateCheckEnabled;

            if (s.ActiveQuotaRule is not null)
            {
                s.ActiveQuotaRule = s.ActiveQuotaRule with { CycleDay = billingCycleDay };
            }

            s.Sanitize();
            settingsService.Save(s);
            StartupHelper.SetStartupEnabled(startWithWindows);
            ThemeManager.ApplyTheme(s.Theme);

            StatusMessage = "Settings saved successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    public void ResetToDefaults()
    {
        try
        {
            var def = new SpeedMeterSettings();
            def.Sanitize();
            settingsService.Save(def);
            StartupHelper.SetStartupEnabled(false);
            ThemeManager.ApplyTheme("Dark");
            LoadCurrent();
            StatusMessage = "Settings reset to defaults.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed: {ex.Message}";
        }
    }

    private async Task PruneHistoryAsync()
    {
        if (historyService is null)
        {
            StatusMessage = "History service unavailable.";
            return;
        }

        var days = DataRetentionDays > 0 ? DataRetentionDays : 90;
        var cutoffUtcMs = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();

        try
        {
            var pruned = await historyService.PruneHistoryAsync(cutoffUtcMs);
            StatusMessage = $"Pruning complete. {pruned} old interval records removed (older than {days} days).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pruning error: {ex.Message}";
        }
    }

    private async Task ClearAllHistoryAsync()
    {
        if (historyService is null)
        {
            StatusMessage = "History service unavailable.";
            return;
        }

        try
        {
            await historyService.ClearAllHistoryAsync();
            StatusMessage = "All historical network usage and downtime ledgers cleared successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to clear history: {ex.Message}";
        }
    }

    private void ExportConfig()
    {
        var svc = configService ?? new ConfigExportImportService();
        var dlg = new SaveFileDialog
        {
            Title = "Export SpeedMeter Pro Configuration",
            Filter = "JSON Configuration (*.json)|*.json",
            FileName = $"speedmeter_config_{DateTime.UtcNow:yyyyMMdd}.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var current = settingsService.CurrentSettings;
                svc.ExportToFile(dlg.FileName, current);
                StatusMessage = $"Configuration exported successfully to {Path.GetFileName(dlg.FileName)}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }
    }

    private void ImportConfig()
    {
        var svc = configService ?? new ConfigExportImportService();
        var dlg = new OpenFileDialog
        {
            Title = "Import SpeedMeter Pro Configuration",
            Filter = "JSON Configuration (*.json)|*.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var current = settingsService.CurrentSettings;
                var result = svc.ImportFromFile(dlg.FileName, current);
                if (result.Success && result.Settings is not null)
                {
                    settingsService.Save(result.Settings);
                    LoadCurrent();
                    ThemeManager.ApplyTheme(result.Settings.Theme);
                    StatusMessage = result.Message;
                }
                else
                {
                    StatusMessage = $"Import rejected: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
            }
        }
    }
}
