using System;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    private readonly IUsageHistoryService historyService;
    private readonly IQuotaService quotaService;
    private readonly ISettingsService settingsService;
    private readonly Collector collector;

    private string downloadRateText = "—";
    private string uploadRateText = "—";
    private string sessionRxText = "Session: 0 B";
    private string sessionTxText = "Session: 0 B";
    private string scopeText = "Resolving scope…";
    private string collectorStateText = "Starting";
    private bool isDegraded;
    private string? degradedReason;
    private bool isBits;

    // Today Historical Summary
    private string todayRxText = "0 B";
    private string todayTxText = "0 B";
    private string todayTotalText = "0 B";
    private string todayCoverageText = "0% · No Coverage";
    private double todayCoverageRatio;
    private CompletenessState todayCompleteness = CompletenessState.NoCoverage;

    // Quota Summary
    private bool quotaEnabled;
    private string quotaSummaryText = "Quota not configured";
    private string quotaRemainingText = "—";
    private double quotaPercent;
    private string quotaBadgeText = "Off";

    public string DownloadRateText { get => downloadRateText; private set => SetProperty(ref downloadRateText, value); }
    public string UploadRateText { get => uploadRateText; private set => SetProperty(ref uploadRateText, value); }
    public string SessionRxText { get => sessionRxText; private set => SetProperty(ref sessionRxText, value); }
    public string SessionTxText { get => sessionTxText; private set => SetProperty(ref sessionTxText, value); }
    public string ScopeText { get => scopeText; private set => SetProperty(ref scopeText, value); }
    public string CollectorStateText { get => collectorStateText; private set => SetProperty(ref collectorStateText, value); }
    public bool IsDegraded { get => isDegraded; private set => SetProperty(ref isDegraded, value); }
    public string? DegradedReason { get => degradedReason; private set => SetProperty(ref degradedReason, value); }
    public bool IsBits { get => isBits; set { if (SetProperty(ref isBits, value)) RefreshRateDisplay(); } }

    public string TodayRxText { get => todayRxText; private set => SetProperty(ref todayRxText, value); }
    public string TodayTxText { get => todayTxText; private set => SetProperty(ref todayTxText, value); }
    public string TodayTotalText { get => todayTotalText; private set => SetProperty(ref todayTotalText, value); }
    public string TodayCoverageText { get => todayCoverageText; private set => SetProperty(ref todayCoverageText, value); }
    public double TodayCoverageRatio { get => todayCoverageRatio; private set => SetProperty(ref todayCoverageRatio, value); }
    public CompletenessState TodayCompleteness { get => todayCompleteness; private set => SetProperty(ref todayCompleteness, value); }

    public bool QuotaEnabled { get => quotaEnabled; private set => SetProperty(ref quotaEnabled, value); }
    public string QuotaSummaryText { get => quotaSummaryText; private set => SetProperty(ref quotaSummaryText, value); }
    public string QuotaRemainingText { get => quotaRemainingText; private set => SetProperty(ref quotaRemainingText, value); }
    public double QuotaPercent { get => quotaPercent; private set => SetProperty(ref quotaPercent, value); }
    public string QuotaBadgeText { get => quotaBadgeText; private set => SetProperty(ref quotaBadgeText, value); }

    private Reading? latestReading;

    public OverviewViewModel(
        IUsageHistoryService historyService,
        IQuotaService quotaService,
        ISettingsService settingsService,
        Collector collector)
    {
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        this.quotaService = quotaService ?? throw new ArgumentNullException(nameof(quotaService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));

        isBits = settingsService.CurrentSettings.DisplayUnits == "Mbps";
    }

    public void UpdateSnapshot(CollectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        latestReading = snapshot.Reading;

        CollectorStateText = snapshot.State.ToString();
        IsDegraded = snapshot.State == CollectorState.Degraded || (snapshot.StorageStatus?.Health == Storage.StorageHealth.Degraded);
        DegradedReason = snapshot.StorageStatus?.DegradedReason;

        var decision = snapshot.ScopeDecision;
        if (decision is not null)
        {
            var mode = decision.Mode == ScopeMode.AutoPrimary ? "AutoPrimary" : "Manual";
            var iface = decision.PrimaryAdapterId is not null ? decision.Summary : "None";
            ScopeText = $"{mode}: {iface}";
        }

        RefreshRateDisplay();

        SessionRxText = $"Session Rx: {MetricFormat.Bytes(snapshot.Reading.Received)}";
        SessionTxText = $"Session Tx: {MetricFormat.Bytes(snapshot.Reading.Sent)}";
    }

    private void RefreshRateDisplay()
    {
        if (latestReading is null)
        {
            DownloadRateText = "—";
            UploadRateText = "—";
            return;
        }

        if (latestReading.State == ReadingState.Live)
        {
            DownloadRateText = MetricFormat.Rate(latestReading.Download, isBits);
            UploadRateText = MetricFormat.Rate(latestReading.Upload, isBits);
        }
        else
        {
            DownloadRateText = latestReading.State switch
            {
                ReadingState.Measuring => "Measuring…",
                ReadingState.Paused => "Paused",
                ReadingState.Discontinuity => "Discontinuity",
                ReadingState.Unavailable => "Unavailable",
                _ => "—"
            };
            UploadRateText = DownloadRateText;
        }
    }

    public async Task RefreshHistoryAndQuotaAsync(CancellationToken ct = default)
    {
        var settings = settingsService.CurrentSettings;
        var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        var nowUtc = DateTimeOffset.UtcNow;
        var cycleDay = settings.ActiveQuotaRule?.CycleDay ?? 1;

        try
        {
            var summary = await historyService.GetPeriodSummaryAsync(
                QueryPeriodType.Today,
                tzId,
                nowUtc,
                cycleDay: cycleDay,
                ct: ct).ConfigureAwait(true);

            TodayRxText = MetricFormat.Bytes(summary.BytesRx);
            TodayTxText = MetricFormat.Bytes(summary.BytesTx);
            TodayTotalText = MetricFormat.Bytes(summary.TotalBytes);
            TodayCoverageRatio = summary.CoverageRatio;
            TodayCompleteness = summary.Completeness;
            TodayCoverageText = $"{summary.CoverageRatio * 100:F1}% · {summary.Completeness}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TodayCoverageText = $"Error: {ex.Message}";
        }

        try
        {
            var rule = settings.ActiveQuotaRule;
            if (rule is { Enabled: true })
            {
                QuotaEnabled = true;
                var eval = await quotaService.EvaluateCurrentCycleAsync(rule, nowUtc, ct).ConfigureAwait(true);

                var usedFormatted = MetricFormat.Bytes(eval.EffectiveUsedBytes);
                var limitFormatted = MetricFormat.Bytes(rule.LimitBytes);
                QuotaPercent = Math.Min(100.0, eval.PercentUsed);
                QuotaSummaryText = $"{usedFormatted} / {limitFormatted} ({QuotaPercent:F1}%)";
                QuotaRemainingText = eval.IsExceeded
                    ? $"Over by {MetricFormat.Bytes(eval.OverageBytes)}"
                    : $"Remaining: {MetricFormat.Bytes(eval.RemainingBytes)}";

                QuotaBadgeText = eval.IsExceeded ? "100% Exceeded" : $"{QuotaPercent:F0}%";
            }
            else
            {
                QuotaEnabled = false;
                QuotaSummaryText = "Quota monitoring disabled";
                QuotaRemainingText = "—";
                QuotaPercent = 0;
                QuotaBadgeText = "Off";
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            QuotaSummaryText = "Quota unavailable";
        }
    }
}
