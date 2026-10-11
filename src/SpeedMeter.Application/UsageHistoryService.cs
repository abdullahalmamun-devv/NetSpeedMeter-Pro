using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Application;

public interface IUsageHistoryService
{
    Task<HistoricalSummary> GetPeriodSummaryAsync(
        QueryPeriodType period,
        string timezoneId,
        DateTimeOffset nowUtc,
        int cycleDay = 1,
        DayOfWeek startOfWeek = DayOfWeek.Monday,
        string? scopeRevisionId = null,
        string? networkProfileId = null,
        CancellationToken ct = default);

    Task<HistoricalSummary> GetCustomSummaryAsync(
        HistoricalRange range,
        string timezoneId,
        string? scopeRevisionId = null,
        string? networkProfileId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(
        HistoricalRange range,
        long? cursorStartUtcMs = null,
        string? cursorIntervalId = null,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetAvailabilityAsync(CancellationToken ct = default);
    Task<int> PruneHistoryAsync(long beforeUtcMs, CancellationToken ct = default) => Task.FromResult(0);
    Task ClearAllHistoryAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class UsageHistoryService : IUsageHistoryService
{
    private readonly IStorageEngine storage;

    public UsageHistoryService(IStorageEngine storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public Task<int> PruneHistoryAsync(long beforeUtcMs, CancellationToken ct = default) =>
        storage.PruneHistoryAsync(beforeUtcMs, ct);

    public Task ClearAllHistoryAsync(CancellationToken ct = default) =>
        storage.ClearAllHistoryAsync(ct);

    public Task<HistoricalSummary> GetPeriodSummaryAsync(
        QueryPeriodType period,
        string timezoneId,
        DateTimeOffset nowUtc,
        int cycleDay = 1,
        DayOfWeek startOfWeek = DayOfWeek.Monday,
        string? scopeRevisionId = null,
        string? networkProfileId = null,
        CancellationToken ct = default)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var range = CalendarRangeResolver.ResolveRange(period, tz, nowUtc, cycleDay, startOfWeek);
        return storage.GetUsageSummaryAsync(range, timezoneId, scopeRevisionId, networkProfileId, ct);
    }

    public Task<HistoricalSummary> GetCustomSummaryAsync(
        HistoricalRange range,
        string timezoneId,
        string? scopeRevisionId = null,
        string? networkProfileId = null,
        CancellationToken ct = default)
    {
        return storage.GetUsageSummaryAsync(range, timezoneId, scopeRevisionId, networkProfileId, ct);
    }

    public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(
        HistoricalRange range,
        long? cursorStartUtcMs = null,
        string? cursorIntervalId = null,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        return storage.GetPagedIntervalsAsync(range, cursorStartUtcMs, cursorIntervalId, pageSize, ct);
    }

    public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetAvailabilityAsync(CancellationToken ct = default)
    {
        return storage.GetHistoryAvailabilityAsync(ct);
    }
}
