using System;
using System.Threading.Tasks;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class QuickPanelViewModel : ViewModelBase
{
    private readonly Collector collector;
    private readonly IUsageHistoryService historyService;
    private readonly ISettingsService settingsService;
    private readonly ConnectivityService? connectivityService;
    private readonly Action openDashboardAction;

    private string downloadRateText = "—";
    private string uploadRateText = "—";
    private string todayUsageText = "Today: 0 B";
    private string scopeText = "Scope: —";
    private string connectivityText = "Local-Only";
    private bool isPaused;
    private bool isBits;

    public string DownloadRateText { get => downloadRateText; private set => SetProperty(ref downloadRateText, value); }
    public string UploadRateText { get => uploadRateText; private set => SetProperty(ref uploadRateText, value); }
    public string TodayUsageText { get => todayUsageText; private set => SetProperty(ref todayUsageText, value); }
    public string ScopeText { get => scopeText; private set => SetProperty(ref scopeText, value); }
    public string ConnectivityText { get => connectivityText; private set => SetProperty(ref connectivityText, value); }
    public bool IsPaused { get => isPaused; private set => SetProperty(ref isPaused, value); }

    public ICommand TogglePauseCommand { get; }
    public ICommand OpenDashboardCommand { get; }

    public QuickPanelViewModel(
        Collector collector,
        IUsageHistoryService historyService,
        ISettingsService settingsService,
        Action openDashboardAction,
        ConnectivityService? connectivityService = null)
    {
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.openDashboardAction = openDashboardAction ?? throw new ArgumentNullException(nameof(openDashboardAction));
        this.connectivityService = connectivityService;

        isBits = settingsService.CurrentSettings.DisplayUnits == "Mbps";
        isPaused = collector.Paused;

        TogglePauseCommand = new RelayCommand(() =>
        {
            collector.TogglePause();
            IsPaused = collector.Paused;
        });

        OpenDashboardCommand = new RelayCommand(openDashboardAction);
    }

    public void UpdateSnapshot(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IsPaused = collector.Paused;

        if (connectivityService is not null && settingsService.CurrentSettings.ConnectionChecksEnabled)
        {
            var snap = connectivityService.Snapshot;
            ConnectivityText = snap.State switch
            {
                ConnectivityState.Reachable => "Reachable",
                ConnectivityState.Limited => "Checks Failing",
                ConnectivityState.NoLocalConnection => "No Local Link",
                ConnectivityState.SignInSuspected => "Portal Sign-In",
                _ => "Checking…"
            };
        }
        else
        {
            ConnectivityText = "Local-Only";
        }

        var reading = snapshot.Reading;
        if (reading.State == ReadingState.Live)
        {
            DownloadRateText = MetricFormat.Rate(reading.Download, isBits);
            UploadRateText = MetricFormat.Rate(reading.Upload, isBits);
        }
        else
        {
            DownloadRateText = reading.State switch
            {
                ReadingState.Measuring => "Measuring…",
                ReadingState.Paused => "Paused",
                ReadingState.Discontinuity => "Discontinuity",
                ReadingState.Unavailable => "Unavailable",
                _ => "—"
            };
            UploadRateText = DownloadRateText;
        }

        var decision = snapshot.ScopeDecision;
        if (decision is not null)
        {
            var iface = decision.PrimaryAdapterId is not null ? decision.Summary : "None";
            ScopeText = $"Scope: {iface}";
        }
    }

    public async Task RefreshTodayUsageAsync()
    {
        try
        {
            var settings = settingsService.CurrentSettings;
            var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
            var cycleDay = settings.ActiveQuotaRule?.CycleDay ?? 1;

            var summary = await historyService.GetPeriodSummaryAsync(
                QueryPeriodType.Today,
                tzId,
                DateTimeOffset.UtcNow,
                cycleDay: cycleDay).ConfigureAwait(true);

            TodayUsageText = $"Today: {MetricFormat.Bytes(summary.TotalBytes)} ({summary.CoverageRatio * 100:F0}% cov)";
        }
        catch
        {
            TodayUsageText = "Today: —";
        }
    }
}
