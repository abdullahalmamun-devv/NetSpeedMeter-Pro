using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;

namespace SpeedMeter.Storage;

public interface IStorageEngine : IAsyncDisposable, IDisposable
{
    StorageStatus Status { get; }

    Task RecordSessionStartAsync(SessionRecord session, CancellationToken ct = default);
    Task RecordSessionEndAsync(string sessionId, DateTimeOffset endedAtUtc, bool endedCleanly, string? stopReason, CancellationToken ct = default);
    Task RecordClockSegmentAsync(ClockSegmentRecord segment, CancellationToken ct = default);
    Task RecordScopeRevisionAsync(ScopeRevisionRecord revision, CancellationToken ct = default);
    Task RecordScopeMembershipAsync(ScopeMembershipRecord membership, CancellationToken ct = default);
    Task RecordCheckpointAsync(CounterCheckpointRecord checkpoint, CancellationToken ct = default);
    Task IngestIntervalAsync(UsageIntervalRecord interval, CounterCheckpointRecord? checkpoint = null, CancellationToken ct = default);
    Task IngestGapAsync(UsageIntervalRecord gap, CancellationToken ct = default);

    Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default);
    Task<long?> GetLastDurableTimestampUtcMsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default);
    Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default);

    Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetHistoryAvailabilityAsync(CancellationToken ct = default);
    Task<HistoricalSummary> GetUsageSummaryAsync(HistoricalRange range, string reportingTimezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default);
    Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default);
    Task<int> ComputeDailyRollupsAsync(HistoricalRange range, string reportingTimezoneId, CancellationToken ct = default);
    Task RecordQuotaThresholdEventAsync(QuotaThresholdEvent record, CancellationToken ct = default);
    Task<IReadOnlyList<double>> GetRecordedThresholdsAsync(string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default);

    Task FlushAsync(TimeSpan timeout, CancellationToken ct = default);
}
