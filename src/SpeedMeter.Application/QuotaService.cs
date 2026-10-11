using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Application;

public interface IQuotaService
{
    Task<QuotaEvaluation> EvaluateCurrentCycleAsync(
        QuotaRule rule,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    Task RecordCrossedThresholdsAsync(
        QuotaRule rule,
        string cycleIdentifier,
        IReadOnlyList<int> newlyCrossedBps,
        long crossedAtUtcMs,
        long effectiveUsageBytes,
        CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(
        string ruleId,
        int ruleRevision,
        string cycleIdentifier,
        CancellationToken ct = default);
}

public sealed class QuotaService : IQuotaService
{
    private readonly IStorageEngine storage;

    public QuotaService(IStorageEngine storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<QuotaEvaluation> EvaluateCurrentCycleAsync(
        QuotaRule rule,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(rule.ReportingTimezoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var cycleRange = CalendarRangeResolver.GetBillingCycle(tz, nowUtc, rule.CycleDay);
        var cycleId = BillingCycleIdentity.Create(
            rule.RuleId, rule.Revision, cycleRange.StartUtcMs, cycleRange.EndUtcMs, rule.ReportingTimezoneId);

        var previousThresholds = await storage.GetRecordedThresholdsAsync(
            rule.RuleId, rule.Revision, cycleId, ct).ConfigureAwait(false);

        var summary = await storage.GetUsageSummaryAsync(
            cycleRange, rule.ReportingTimezoneId, ct: ct).ConfigureAwait(false);

        return QuotaEvaluator.Evaluate(rule, summary.BytesRx, summary.BytesTx, previousThresholds);
    }

    public async Task RecordCrossedThresholdsAsync(
        QuotaRule rule,
        string cycleIdentifier,
        IReadOnlyList<int> newlyCrossedBps,
        long crossedAtUtcMs,
        long effectiveUsageBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(newlyCrossedBps);

        foreach (var bps in newlyCrossedBps)
        {
            var evt = new QuotaThresholdEvent(
                Guid.NewGuid().ToString("N"),
                rule.RuleId,
                rule.Revision,
                cycleIdentifier,
                bps,
                crossedAtUtcMs,
                effectiveUsageBytes);

            await storage.RecordQuotaThresholdEventAsync(evt, ct).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(
        string ruleId,
        int ruleRevision,
        string cycleIdentifier,
        CancellationToken ct = default)
    {
        return storage.GetRecordedThresholdsAsync(ruleId, ruleRevision, cycleIdentifier, ct);
    }
}
