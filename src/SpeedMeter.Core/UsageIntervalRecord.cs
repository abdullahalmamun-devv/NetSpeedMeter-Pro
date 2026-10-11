using System;

namespace SpeedMeter.Core;

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
