using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed class QuotaViewModel : ViewModelBase
{
    private readonly IQuotaService quotaService;
    private readonly ISettingsService settingsService;

    private bool ruleEnabled;
    private double quotaGigabytes = 100;
    private QuotaDirectionMode directionMode = QuotaDirectionMode.Combined;
    private int cycleDay = 1;
    private double openingBalanceGigabytes = 0;
    private string thresholdsText = "80%, 90%, 100%";
    private string previewDatesText = "Calculating cycle…";
    private string effectiveUsageText = "0 B";
    private string remainingBytesText = "—";
    private string percentageUsedText = "0%";
    private double progressValue = 0;
    private string? statusMessage;

    public bool RuleEnabled { get => ruleEnabled; set { if (SetProperty(ref ruleEnabled, value)) UpdatePreview(); } }
    public double QuotaGigabytes { get => quotaGigabytes; set { if (SetProperty(ref quotaGigabytes, value)) UpdatePreview(); } }
    public QuotaDirectionMode DirectionMode { get => directionMode; set => SetProperty(ref directionMode, value); }
    public int CycleDay { get => cycleDay; set { if (SetProperty(ref cycleDay, Math.Clamp(value, 1, 31))) UpdatePreview(); } }
    public double OpeningBalanceGigabytes { get => openingBalanceGigabytes; set => SetProperty(ref openingBalanceGigabytes, Math.Max(0, value)); }
    public string ThresholdsText { get => thresholdsText; set => SetProperty(ref thresholdsText, value); }

    public string PreviewDatesText { get => previewDatesText; private set => SetProperty(ref previewDatesText, value); }
    public string EffectiveUsageText { get => effectiveUsageText; private set => SetProperty(ref effectiveUsageText, value); }
    public string RemainingBytesText { get => remainingBytesText; private set => SetProperty(ref remainingBytesText, value); }
    public string PercentageUsedText { get => percentageUsedText; private set => SetProperty(ref percentageUsedText, value); }
    public double ProgressValue { get => progressValue; private set => SetProperty(ref progressValue, value); }
    public string? StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }

    public ICommand SaveRuleCommand { get; }
    public ICommand RefreshCommand { get; }

    public QuotaViewModel(IQuotaService quotaService, ISettingsService settingsService)
    {
        this.quotaService = quotaService ?? throw new ArgumentNullException(nameof(quotaService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        LoadFromSettings();

        SaveRuleCommand = new RelayCommand(SaveRule);
        RefreshCommand = new RelayCommand(async () => await RefreshEvaluationAsync());
    }

    public static string FormatBasisPoints(int bps)
    {
        return $"{bps / 100.0:F1}%";
    }

    public static int[] ParseBasisPoints(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [8000, 9000, 10000];

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>();
        foreach (var p in parts)
        {
            var cleaned = p.TrimEnd('%').Trim();
            if (double.TryParse(cleaned, out var pct))
            {
                var bps = (int)Math.Round(pct * 100.0);
                if (bps is > 0 and <= 20000) list.Add(bps);
            }
        }
        return list.Count > 0 ? list.Distinct().OrderBy(x => x).ToArray() : [8000, 9000, 10000];
    }

    public void LoadFromSettings()
    {
        var settings = settingsService.CurrentSettings;
        var rule = settings.ActiveQuotaRule;
        if (rule is not null)
        {
            ruleEnabled = rule.Enabled;
            quotaGigabytes = (double)rule.LimitBytes / 1_000_000_000.0;
            directionMode = rule.DirectionMode;
            cycleDay = rule.CycleDay;
            openingBalanceGigabytes = (double)rule.OpeningBalanceBytes / 1_000_000_000.0;
            thresholdsText = string.Join(", ", rule.WarningThresholdsBps.Select(FormatBasisPoints));
        }
        else
        {
            ruleEnabled = false;
            quotaGigabytes = 100;
            directionMode = QuotaDirectionMode.Combined;
            cycleDay = 1;
            openingBalanceGigabytes = 0;
            thresholdsText = "80%, 90%, 100%";
        }

        UpdatePreview();
        _ = RefreshEvaluationAsync();
    }

    public void UpdatePreview()
    {
        var settings = settingsService.CurrentSettings;
        var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { tz = TimeZoneInfo.Local; }

        var nowUtc = DateTimeOffset.UtcNow;
        var currentRange = CalendarRangeResolver.GetBillingCycle(tz, nowUtc, cycleDay);
        var nextRange = CalendarRangeResolver.GetBillingCycle(tz, DateTimeOffset.FromUnixTimeMilliseconds(currentRange.EndUtcMs + 1000), cycleDay);

        var curStart = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(currentRange.StartUtcMs), tz);
        var curEnd = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(currentRange.EndUtcMs), tz);
        var nextStart = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nextRange.StartUtcMs), tz);
        var nextEnd = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nextRange.EndUtcMs), tz);

        PreviewDatesText = $"Current Cycle: {curStart:yyyy-MM-dd} → {curEnd:yyyy-MM-dd}\nNext Cycle: {nextStart:yyyy-MM-dd} → {nextEnd:yyyy-MM-dd}";
    }

    public async Task RefreshEvaluationAsync(CancellationToken ct = default)
    {
        var rule = BuildCurrentRule();
        var nowUtc = DateTimeOffset.UtcNow;

        try
        {
            var eval = await quotaService.EvaluateCurrentCycleAsync(rule, nowUtc, ct).ConfigureAwait(true);

            EffectiveUsageText = MetricFormat.Bytes(eval.EffectiveUsedBytes);
            RemainingBytesText = eval.IsExceeded
                ? $"Over quota by {MetricFormat.Bytes(eval.OverageBytes)}"
                : $"{MetricFormat.Bytes(eval.RemainingBytes)} remaining";

            var pct = eval.PercentUsed;
            PercentageUsedText = $"{pct:F1}%";
            ProgressValue = Math.Min(100.0, pct);
            StatusMessage = eval.IsExceeded ? "Warning: Quota exceeded!" : "Within quota limit.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Evaluation failed: {ex.Message}";
        }
    }

    public QuotaRule BuildCurrentRule()
    {
        var settings = settingsService.CurrentSettings;
        var tzId = settings.ReportingTimezoneId ?? TimeZoneInfo.Local.Id;
        var limitBytes = (long)Math.Max(1, Math.Round(quotaGigabytes * 1_000_000_000.0));
        var openingBytes = (long)Math.Max(0, Math.Round(openingBalanceGigabytes * 1_000_000_000.0));
        var thresholds = ParseBasisPoints(thresholdsText);

        var existing = settings.ActiveQuotaRule;
        var revision = (existing?.Revision ?? 0) + 1;
        var ruleId = existing?.RuleId ?? Guid.NewGuid().ToString("N");

        return new QuotaRule(
            RuleId: ruleId,
            Revision: revision,
            Enabled: ruleEnabled,
            PeriodType: QuotaPeriodType.MonthlyBillingCycle,
            CycleDay: cycleDay,
            DirectionMode: directionMode,
            LimitBytes: limitBytes,
            WarningThresholdsBps: thresholds,
            ReportingTimezoneId: tzId,
            OpeningBalanceBytes: openingBytes);
    }

    public void SaveRule()
    {
        try
        {
            var rule = BuildCurrentRule();
            rule.Validate();

            var current = settingsService.CurrentSettings;
            current.ActiveQuotaRule = rule;
            current.Sanitize();
            settingsService.Save(current);

            StatusMessage = "Quota configuration saved successfully.";
            _ = RefreshEvaluationAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}
