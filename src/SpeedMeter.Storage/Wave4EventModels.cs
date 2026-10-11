using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;

namespace SpeedMeter.Storage;

/// <summary>Evidence-bearing timeline event. State and source are labels, not inferred root causes.</summary>
public sealed record ConnectivityEventRecord(
    string EventId,
    string EventType,
    string EvidenceSource,
    string? ScopeId,
    string? NetworkProfileId,
    string State,
    long ObservedAtUtcMs,
    long? ConfirmedAtUtcMs,
    string Certainty,
    string? Detail);

/// <summary>V1 timeline storage kept apart from the existing accounting engine contract.</summary>
public interface IWave4EventStore
{
    Task RecordConnectivityEventAsync(ConnectivityEventRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectivityEventRecord>> GetConnectivityEventsAsync(long startUtcMs, long endUtcMs, int limit = 500, CancellationToken ct = default);
    Task<AlertIncidentState?> GetAlertStateAsync(string key, CancellationToken ct = default);
    Task<bool> TryCommitAlertAsync(string key, long expectedVersion, AlertIncidentState nextState, AlertEvent alertEvent, CancellationToken ct = default);
    Task<bool> TryRecordQuotaAlertAsync(AlertEvent alertEvent, CancellationToken ct = default);
    Task<IReadOnlyList<AlertEvent>> GetAlertEventsAsync(long startUtcMs, long endUtcMs, int limit = 500, CancellationToken ct = default);
}
