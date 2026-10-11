using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeedMeter.Core;

public enum QuotaDirectionMode
{
    Combined,
    DownloadOnly,
    UploadOnly
}

public enum QuotaPeriodType
{
    MonthlyBillingCycle,
    Daily,
    Weekly
}

/// <summary>
/// Canonical billing cycle identifier generator.
/// Stable across locales, operating systems, and process restarts.
/// </summary>
public static class BillingCycleIdentity
{
    public static string Create(string ruleId, int ruleRevision, long startUtcMs, long endUtcMs, string timezoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timezoneId);
        var safeTz = timezoneId.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
        return $"{ruleId}_r{ruleRevision}_{startUtcMs}_{endUtcMs}_{safeTz}";
    }
}

/// <summary>
/// Domain model for quota configuration rules.
/// Warning thresholds are specified in basis points (e.g. 8000 = 80.00%, 10000 = 100.00%).
/// </summary>
public sealed record QuotaRule(
    string RuleId,
    int Revision,
    bool Enabled,
    QuotaPeriodType PeriodType,
    int CycleDay,
    QuotaDirectionMode DirectionMode,
    long LimitBytes,
    IReadOnlyList<int> WarningThresholdsBps,
    string ReportingTimezoneId,
    long OpeningBalanceBytes = 0,
    long EffectiveFromUtcMs = 0)
{
    /// <summary>
    /// PRD §11.2 authoritative default warning thresholds: 80%, 90%, 100% (8000, 9000, 10000 bps).
    /// </summary>
    public static readonly IReadOnlyList<int> DefaultThresholdsBps = [8000, 9000, 10000];

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RuleId);
        if (Revision <= 0) throw new ArgumentOutOfRangeException(nameof(Revision), "Revision must be greater than 0.");
        if (CycleDay < 1 || CycleDay > 31) throw new ArgumentOutOfRangeException(nameof(CycleDay), "CycleDay must be between 1 and 31.");
        if (LimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(LimitBytes), "LimitBytes must be strictly positive.");
        if (OpeningBalanceBytes < 0) throw new ArgumentOutOfRangeException(nameof(OpeningBalanceBytes), "OpeningBalanceBytes cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(ReportingTimezoneId);
        ArgumentNullException.ThrowIfNull(WarningThresholdsBps);

        foreach (var t in WarningThresholdsBps)
        {
            if (t <= 0 || t > 50000)
            {
                throw new ArgumentOutOfRangeException(nameof(WarningThresholdsBps), $"Threshold {t} bps must be between 1 and 50000 (0.01% to 500%).");
            }
        }

        if (WarningThresholdsBps.Distinct().Count() != WarningThresholdsBps.Count)
        {
            throw new ArgumentException("Duplicate warning thresholds are not permitted.", nameof(WarningThresholdsBps));
        }
    }

    /// <summary>
    /// Creates a revised rule with incremented revision number and optional updated parameters.
    /// Preserves immutable identity of prior revision.
    /// </summary>
    public QuotaRule CreateRevision(
        long? newLimitBytes = null,
        IReadOnlyList<int>? newThresholdsBps = null,
        int? newCycleDay = null,
        QuotaDirectionMode? newDirectionMode = null,
        long? newOpeningBalanceBytes = null,
        bool? newEnabled = null)
    {
        return this with
        {
            Revision = this.Revision + 1,
            LimitBytes = newLimitBytes ?? this.LimitBytes,
            WarningThresholdsBps = newThresholdsBps ?? this.WarningThresholdsBps,
            CycleDay = newCycleDay ?? this.CycleDay,
            DirectionMode = newDirectionMode ?? this.DirectionMode,
            OpeningBalanceBytes = newOpeningBalanceBytes ?? this.OpeningBalanceBytes,
            Enabled = newEnabled ?? this.Enabled
        };
    }
}

/// <summary>
/// Domain model for quota evaluation results.
/// </summary>
public sealed record QuotaEvaluation(
    string RuleId,
    int RuleRevision,
    long RecordedBytes,
    long OpeningBalanceBytes,
    long EffectiveUsedBytes,
    long LimitBytes,
    long RemainingBytes,
    double UsedRatio,
    bool IsExceeded,
    IReadOnlyList<int> CrossedThresholdsBps,
    IReadOnlyList<int> NewlyCrossedThresholdsBps)
{
    public long OverageBytes => Math.Max(0L, EffectiveUsedBytes - LimitBytes);
    public double PercentUsed => LimitBytes > 0 ? (double)EffectiveUsedBytes / LimitBytes * 100.0 : 0.0;
}

/// <summary>
/// Persistent record of a triggered quota threshold event to ensure deduplication across restarts.
/// Uses exact integer basis points (e.g. 8000 = 80.00%) for deterministic uniqueness identity.
/// </summary>
public sealed record QuotaThresholdEvent(
    string EventId,
    string RuleId,
    int RuleRevision,
    string CycleIdentifier,
    int ThresholdBps,
    long CrossedAtUtcMs,
    long EffectiveUsageBytes);

/// <summary>
/// Pure evaluation logic for quota rules.
/// </summary>
public static class QuotaEvaluator
{
    /// <summary>
    /// Evaluates a quota rule against recorded traffic and previously emitted threshold events.
    /// </summary>
    public static QuotaEvaluation Evaluate(
        QuotaRule rule,
        long recordedRx,
        long recordedTx,
        IReadOnlyList<int> previouslyEmittedThresholdsBps)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();

        // Select traffic based on direction mode
        var recordedApplicable = rule.DirectionMode switch
        {
            QuotaDirectionMode.DownloadOnly => recordedRx,
            QuotaDirectionMode.UploadOnly => recordedTx,
            QuotaDirectionMode.Combined => checked(recordedRx + recordedTx),
            _ => checked(recordedRx + recordedTx)
        };

        var effectiveUsed = checked(recordedApplicable + rule.OpeningBalanceBytes);
        var remaining = effectiveUsed >= rule.LimitBytes ? 0L : rule.LimitBytes - effectiveUsed;
        var ratio = rule.LimitBytes > 0 ? (double)effectiveUsed / rule.LimitBytes : 0.0;
        var isExceeded = effectiveUsed >= rule.LimitBytes;

        if (!rule.Enabled)
        {
            // PRD: Do not return misleading zeros for recorded usage when rule is disabled.
            // Recorded traffic remains truthful; alerts/notifications are simply suppressed.
            return new QuotaEvaluation(
                rule.RuleId,
                rule.Revision,
                recordedApplicable,
                rule.OpeningBalanceBytes,
                effectiveUsed,
                rule.LimitBytes,
                remaining,
                ratio,
                isExceeded,
                [],
                []);
        }

        // Evaluate thresholds using exact Int128 arithmetic to eliminate 64-bit overflow and rounding drift
        var crossed = new List<int>();
        var newlyCrossed = new List<int>();

        var sortedThresholds = rule.WarningThresholdsBps.OrderBy(t => t).ToList();
        var prevFiredSet = new HashSet<int>(previouslyEmittedThresholdsBps ?? []);

        foreach (var thBps in sortedThresholds)
        {
            // Target bytes = floor((Int128)LimitBytes * thBps / 10000)
            var targetBytes = (long)(((Int128)rule.LimitBytes * thBps) / 10000);

            if (effectiveUsed >= targetBytes)
            {
                crossed.Add(thBps);
                if (!prevFiredSet.Contains(thBps))
                {
                    newlyCrossed.Add(thBps);
                }
            }
        }

        return new QuotaEvaluation(
            rule.RuleId,
            rule.Revision,
            recordedApplicable,
            rule.OpeningBalanceBytes,
            effectiveUsed,
            rule.LimitBytes,
            remaining,
            ratio,
            isExceeded,
            crossed,
            newlyCrossed);
    }
}
