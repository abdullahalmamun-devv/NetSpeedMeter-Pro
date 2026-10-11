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
/// Domain model for quota configuration rules.
/// </summary>
public sealed record QuotaRule(
    string RuleId,
    int Revision,
    bool Enabled,
    QuotaPeriodType PeriodType,
    int CycleDay,
    QuotaDirectionMode DirectionMode,
    long LimitBytes,
    IReadOnlyList<double> WarningThresholds,
    string ReportingTimezoneId,
    long OpeningBalanceBytes = 0,
    long EffectiveFromUtcMs = 0)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RuleId);
        if (Revision <= 0) throw new ArgumentOutOfRangeException(nameof(Revision), "Revision must be greater than 0.");
        if (CycleDay < 1 || CycleDay > 31) throw new ArgumentOutOfRangeException(nameof(CycleDay), "CycleDay must be between 1 and 31.");
        if (LimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(LimitBytes), "LimitBytes must be strictly positive.");
        if (OpeningBalanceBytes < 0) throw new ArgumentOutOfRangeException(nameof(OpeningBalanceBytes), "OpeningBalanceBytes cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(ReportingTimezoneId);
        ArgumentNullException.ThrowIfNull(WarningThresholds);

        foreach (var t in WarningThresholds)
        {
            if (t <= 0.0) throw new ArgumentOutOfRangeException(nameof(WarningThresholds), "Thresholds must be positive.");
        }
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
    IReadOnlyList<double> CrossedThresholds,
    IReadOnlyList<double> NewlyCrossedThresholds);

/// <summary>
/// Persistent record of a triggered quota threshold event to ensure deduplication across restarts.
/// </summary>
public sealed record QuotaThresholdEvent(
    string EventId,
    string RuleId,
    int RuleRevision,
    string CycleIdentifier,
    double ThresholdPercent,
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
        IReadOnlyList<double> previouslyEmittedThresholds)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();

        if (!rule.Enabled)
        {
            return new QuotaEvaluation(
                rule.RuleId,
                rule.Revision,
                0,
                rule.OpeningBalanceBytes,
                rule.OpeningBalanceBytes,
                rule.LimitBytes,
                Math.Max(0L, rule.LimitBytes - rule.OpeningBalanceBytes),
                rule.LimitBytes > 0 ? (double)rule.OpeningBalanceBytes / rule.LimitBytes : 0.0,
                rule.OpeningBalanceBytes >= rule.LimitBytes,
                [],
                []);
        }

        // Select traffic based on direction mode
        var recordedApplicable = rule.DirectionMode switch
        {
            QuotaDirectionMode.DownloadOnly => recordedRx,
            QuotaDirectionMode.UploadOnly => recordedTx,
            QuotaDirectionMode.Combined => checked(recordedRx + recordedTx),
            _ => checked(recordedRx + recordedTx)
        };

        // Enforce PRD §22 & Prompt §44/45: EffectiveUsage = Recorded + OpeningBalance
        var effectiveUsed = checked(recordedApplicable + rule.OpeningBalanceBytes);
        var remaining = effectiveUsed >= rule.LimitBytes ? 0L : rule.LimitBytes - effectiveUsed;
        var ratio = (double)effectiveUsed / rule.LimitBytes;
        var isExceeded = effectiveUsed >= rule.LimitBytes;

        // Evaluate thresholds using Int128 arithmetic to eliminate 64-bit overflow
        var crossed = new List<double>();
        var newlyCrossed = new List<double>();

        var sortedThresholds = rule.WarningThresholds.OrderBy(t => t).ToList();
        var prevFiredSet = new HashSet<double>(previouslyEmittedThresholds ?? []);

        foreach (var th in sortedThresholds)
        {
            // Calculate threshold byte target: floor((Int128)LimitBytes * (Int128)Round(th * 10000) / 10000)
            var thMultiplier = (long)Math.Round(th * 10000.0);
            var targetBytes = (long)(((Int128)rule.LimitBytes * thMultiplier) / 10000);

            if (effectiveUsed >= targetBytes)
            {
                crossed.Add(th);
                if (!prevFiredSet.Contains(th))
                {
                    newlyCrossed.Add(th);
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
