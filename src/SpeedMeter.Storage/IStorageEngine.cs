using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    Task IngestIntervalAsync(UsageIntervalRecord interval, CancellationToken ct = default);
    Task IngestGapAsync(UsageIntervalRecord gap, CancellationToken ct = default);

    Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default);
    Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default);

    Task FlushAsync(TimeSpan timeout, CancellationToken ct = default);
}
