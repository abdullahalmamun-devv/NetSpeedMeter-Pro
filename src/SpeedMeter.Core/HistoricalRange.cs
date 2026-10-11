using System;

namespace SpeedMeter.Core;

/// <summary>
/// Half-open time range [StartUtcMs, EndUtcMs) in UTC milliseconds.
/// Enforces StartUtcMs < EndUtcMs to guarantee non-zero positive duration.
/// </summary>
public readonly record struct HistoricalRange
{
    public long StartUtcMs { get; }
    public long EndUtcMs { get; }

    public long DurationMs => EndUtcMs - StartUtcMs;

    public HistoricalRange(long startUtcMs, long endUtcMs)
    {
        if (endUtcMs <= startUtcMs)
        {
            throw new ArgumentException($"Historical range must have positive duration: start={startUtcMs}, end={endUtcMs}.");
        }
        StartUtcMs = startUtcMs;
        EndUtcMs = endUtcMs;
    }

    /// <summary>Returns true if the timestamp falls within [StartUtcMs, EndUtcMs).</summary>
    public bool Contains(long timestampUtcMs) => timestampUtcMs >= StartUtcMs && timestampUtcMs < EndUtcMs;

    /// <summary>Returns true if the given half-open interval overlaps this range.</summary>
    public bool Overlaps(long itemStartUtcMs, long itemEndUtcMs) =>
        itemStartUtcMs < EndUtcMs && itemEndUtcMs > StartUtcMs;

    /// <summary>Computes the overlap duration in milliseconds between this range and the item interval.</summary>
    public long OverlapDurationMs(long itemStartUtcMs, long itemEndUtcMs)
    {
        var start = Math.Max(StartUtcMs, itemStartUtcMs);
        var end = Math.Min(EndUtcMs, itemEndUtcMs);
        return Math.Max(0L, end - start);
    }
}

public enum QueryPeriodType
{
    Today,
    Yesterday,
    ThisWeek,
    ThisMonth,
    BillingCycle,
    Custom
}

public enum CompletenessState
{
    /// <summary>Full observation across the entire requested period.</summary>
    Complete,
    /// <summary>Partial observation: some intervals or gaps observed, or partially unmonitored.</summary>
    Partial,
    /// <summary>No observed monitoring data exists in this range.</summary>
    NoCoverage,
    /// <summary>Requested range is outside available monitoring history boundary.</summary>
    Unavailable,
    /// <summary>Storage was degraded during query or persistence.</summary>
    StorageDegraded
}

/// <summary>
/// Domain model for historical query summaries.
/// </summary>
public sealed record HistoricalSummary(
    HistoricalRange Range,
    string ReportingTimezoneId,
    long BytesRx,
    long BytesTx,
    long ObservedDurationMs,
    long MissingDurationMs,
    double CoverageRatio,
    CompletenessState Completeness,
    string? ScopeRevisionId = null,
    string? NetworkProfileId = null,
    bool IsEstimated = false)
{
    public long TotalBytes => BytesRx + BytesTx;
}
