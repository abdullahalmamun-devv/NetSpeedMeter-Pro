using System;

namespace SpeedMeter.Storage;

public enum StorageHealth
{
    Healthy,
    Degraded,
    Closed
}

public sealed record StorageStatus(
    StorageHealth Health,
    long EnqueuedCommands,
    long PersistedIntervals,
    long PersistedGaps,
    string? DegradedReason);

public sealed record SessionRecord(
    string SessionId,
    string MonotonicEpoch,
    long StartedAtUtcMs,
    long? EndedAtUtcMs = null,
    string? StopReason = null,
    bool EndedCleanly = false);

public sealed record ClockSegmentRecord(
    string SegmentId,
    string SessionId,
    long MonotonicAnchorTicks,
    long UtcAnchorMs,
    long TicksPerSecond,
    string TimingConfidence,
    string? ChangeReason = null);

public sealed record NetworkProfileRecord(
    string ProfileId,
    string FriendlyName,
    string? OsProfileKey,
    string IdentityConfidence,
    long CreatedAtUtcMs);

public sealed record ScopeRevisionRecord(
    string RevisionId,
    string ScopeId,
    long RevisionNumber,
    string ScopeMode,
    string SourceMethod,
    string SourceVersion,
    string BoundaryDefinition,
    string Validity,
    long CreatedAtUtcMs);

public sealed record ScopeMembershipRecord(
    string MembershipId,
    string ScopeRevisionId,
    string InterfaceId,
    string? ObservedIncarnation,
    string NetworkProfileId,
    string SessionId,
    long StartsAtMonotonicTicks,
    long? EndsAtMonotonicTicks = null);

public sealed record CounterCheckpointRecord(
    string CheckpointId,
    string SessionId,
    string InterfaceId,
    string SourceMethod,
    string? ObservedSourceEpoch,
    long ObservedSequence,
    long MonotonicTicks,
    long ObservedAtUtcMs,
    byte[] CounterRx,
    byte[] CounterTx);

public sealed record UsageIntervalRecord(
    string IntervalId,
    string SourceIntervalKey,
    string SessionId,
    string ClockSegmentId,
    string ScopeRevisionId,
    string NetworkProfileId,
    long StartUtcMs,
    long EndUtcMs,
    long? MonotonicDurationTicks,
    long BytesRx,
    long BytesTx,
    bool IsCoarse,
    bool IsGap,
    string? GapReason,
    string TimingConfidence);

public sealed record DailyRollupRecord(
    long StartUtcMs,
    long EndUtcMs,
    string ReportingTimezone,
    string CalendarRuleRevision,
    string ScopeId,
    string NetworkProfileId,
    long BytesRx,
    long BytesTx,
    long ValidDurationMs,
    long GapDurationMs,
    string? PeakRxIntervalId = null,
    string? PeakTxIntervalId = null);
