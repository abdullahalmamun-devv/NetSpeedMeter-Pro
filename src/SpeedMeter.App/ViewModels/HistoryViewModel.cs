using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.App.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly IUsageHistoryService historyService;
    private readonly ISettingsService settingsService;
    private readonly IExportService exportService;

    private QueryPeriodType selectedPeriod = QueryPeriodType.Today;
    private string rangeDescription = "Today";
    private string totalBytesText = "0 B";
    private string rxBytesText = "0 B";
    private string txBytesText = "0 B";
    private string coverageText = "0% · No Coverage";
    private double coverageRatio;
    private CompletenessState completeness = CompletenessState.NoCoverage;
    private double rxPercent;
    private double txPercent;
    private bool isLoading;
    private bool hasMore;
    private string? exportStatusMessage;

    private HistoricalRange currentRange;
    private long? lastCursorStartUtcMs;
    private string? lastCursorIntervalId;
    private CancellationTokenSource? queryCts;

    public QueryPeriodType SelectedPeriod
    {
        get => selectedPeriod;
        set
        {
            if (SetProperty(ref selectedPeriod, value))
            {
                _ = LoadPeriodAsync(value);
            }
        }
    }

    public string RangeDescription { get => rangeDescription; private set => SetProperty(ref rangeDescription, value); }
    public string TotalBytesText { get => totalBytesText; private set => SetProperty(ref totalBytesText, value); }
    public string RxBytesText { get => rxBytesText; private set => SetProperty(ref rxBytesText, value); }
    public string TxBytesText { get => txBytesText; private set => SetProperty(ref txBytesText, value); }
    public string CoverageText { get => coverageText; private set => SetProperty(ref coverageText, value); }
    public double CoverageRatio { get => coverageRatio; private set => SetProperty(ref coverageRatio, value); }
    public CompletenessState Completeness { get => completeness; private set => SetProperty(ref completeness, value); }
    public double RxPercent { get => rxPercent; private set => SetProperty(ref rxPercent, value); }
    public double TxPercent { get => txPercent; private set => SetProperty(ref txPercent, value); }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public bool HasMore { get => hasMore; private set => SetProperty(ref hasMore, value); }
    public string? ExportStatusMessage { get => exportStatusMessage; private set => SetProperty(ref exportStatusMessage, value); }

    public ObservableCollection<UsageIntervalRecord> Intervals { get; } = [];

    public ICommand SelectPeriodCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportJsonCommand { get; }

    public HistoryViewModel(
        IUsageHistoryService historyService,
        ISettingsService settingsService,
        IExportService exportService)
    {
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        SelectPeriodCommand = new RelayCommand(p =>
        {
            if (p is QueryPeriodType period) SelectedPeriod = period;
            else if (p is string s && Enum.TryParse<QueryPeriodType>(s, out var parsed)) SelectedPeriod = parsed;
        });

        LoadMoreCommand = new RelayCommand(async () => await LoadNextPageAsync(), () => HasMore && !IsLoading);

        ExportCsvCommand = new RelayCommand(async () => await ExportAsync(isJson: false), () => !IsLoading && Intervals.Count > 0);
        ExportJsonCommand = new RelayCommand(async () => await ExportAsync(isJson: true), () => !IsLoading && Intervals.Count > 0);
    }

    public async Task LoadPeriodAsync(QueryPeriodType period)
    {
        // Cancel in-flight query to eliminate stale results race condition
        queryCts?.Cancel();
        queryCts?.Dispose();
        queryCts = new CancellationTokenSource();
        var ct = queryCts.Token;

        IsLoading = true;
        ExportStatusMessage = null;
        Intervals.Clear();
        lastCursorStartUtcMs = null;
        lastCursorIntervalId = null;
        HasMore = false;

        var settings = settingsService.CurrentSettings;
        var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { tz = TimeZoneInfo.Local; }

        var nowUtc = DateTimeOffset.UtcNow;
        var cycleDay = settings.ActiveQuotaRule?.CycleDay ?? 1;
        currentRange = CalendarRangeResolver.ResolveRange(
            period, tz, nowUtc,
            cycleDay: cycleDay,
            startOfWeek: DayOfWeek.Monday);

        RangeDescription = $"{period} ({TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(currentRange.StartUtcMs), tz):yyyy-MM-dd} → {TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(currentRange.EndUtcMs), tz):yyyy-MM-dd})";

        try
        {
            var summary = await historyService.GetPeriodSummaryAsync(
                period, tzId, nowUtc,
                cycleDay: cycleDay,
                ct: ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested) return;

            TotalBytesText = MetricFormat.Bytes(summary.TotalBytes);
            RxBytesText = MetricFormat.Bytes(summary.BytesRx);
            TxBytesText = MetricFormat.Bytes(summary.BytesTx);
            CoverageRatio = summary.CoverageRatio;
            Completeness = summary.Completeness;
            CoverageText = $"{summary.CoverageRatio * 100:F1}% · {summary.Completeness}";

            var sum = summary.TotalBytes;
            if (sum > 0)
            {
                RxPercent = (double)summary.BytesRx / sum * 100.0;
                TxPercent = (double)summary.BytesTx / sum * 100.0;
            }
            else
            {
                RxPercent = 0;
                TxPercent = 0;
            }

            // Load initial page of intervals
            await LoadNextPageAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoading = false;
        }
    }

    public async Task LoadNextPageAsync(CancellationToken ct = default)
    {
        if (currentRange.EndUtcMs <= currentRange.StartUtcMs) return;

        try
        {
            var page = await historyService.GetPagedIntervalsAsync(
                currentRange,
                lastCursorStartUtcMs,
                lastCursorIntervalId,
                pageSize: 50,
                ct: ct).ConfigureAwait(true);

            if (page.Count == 0)
            {
                HasMore = false;
                return;
            }

            foreach (var interval in page)
            {
                Intervals.Add(interval);
            }

            var last = page[^1];
            lastCursorStartUtcMs = last.StartUtcMs;
            lastCursorIntervalId = last.IntervalId;
            HasMore = page.Count == 50;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Failed loading page: {ex.Message}";
        }
    }

    public async Task ExportAsync(bool isJson)
    {
        try
        {
            IsLoading = true;
            ExportStatusMessage = "Exporting…";

            var settings = settingsService.CurrentSettings;
            var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;

            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var ext = isJson ? "json" : "csv";
            var fileName = $"speedmeter_history_{selectedPeriod}_{timestamp}.{ext}";
            var destPath = Path.Combine(downloads, "Downloads", fileName);

            if (!Directory.Exists(Path.GetDirectoryName(destPath)))
            {
                destPath = Path.Combine(Path.GetTempPath(), fileName);
            }

            if (isJson)
            {
                await exportService.ExportUsageIntervalsToJsonAsync(currentRange, destPath, tzId).ConfigureAwait(true);
            }
            else
            {
                await exportService.ExportUsageIntervalsToCsvAsync(currentRange, destPath, tzId).ConfigureAwait(true);
            }

            ExportStatusMessage = $"Saved: {Path.GetFileName(destPath)}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
