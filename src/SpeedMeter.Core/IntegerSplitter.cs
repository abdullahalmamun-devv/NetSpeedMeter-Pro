using System;

namespace SpeedMeter.Core;

/// <summary>
/// Deterministic integer splitting for usage intervals crossing historical query boundaries.
/// Implements cumulative monotonic allocation:
///   CumulativeBytes(t) = floor((Int128)TotalBytes * (t - intervalStart) / intervalDuration)
/// 
/// Invariant:
///   For any contiguous partition of [intervalStart, intervalEnd) into sub-ranges R_1, R_2, ..., R_n:
///   Sum(Allocated(R_i)) == TotalBytes exactly.
///   No byte is lost. No byte is counted twice.
/// </summary>
public static class IntegerSplitter
{
    /// <summary>
    /// Allocates bytes from an interval to a half-open query range [rangeStartUtcMs, rangeEndUtcMs).
    /// </summary>
    public static (long AllocatedRx, long AllocatedTx) Allocate(
        long intervalStartUtcMs,
        long intervalEndUtcMs,
        long bytesRx,
        long bytesTx,
        long rangeStartUtcMs,
        long rangeEndUtcMs)
    {
        // Invalid or non-positive interval: reject safely without division by zero
        var duration = intervalEndUtcMs - intervalStartUtcMs;
        if (duration <= 0 || bytesRx < 0 || bytesTx < 0)
        {
            return (0L, 0L);
        }

        // Check if there is any overlap
        var overlapStart = Math.Max(intervalStartUtcMs, rangeStartUtcMs);
        var overlapEnd = Math.Min(intervalEndUtcMs, rangeEndUtcMs);

        if (overlapEnd <= overlapStart)
        {
            return (0L, 0L);
        }

        // If range covers the entire interval, return total bytes exactly
        if (overlapStart == intervalStartUtcMs && overlapEnd == intervalEndUtcMs)
        {
            return (bytesRx, bytesTx);
        }

        var rx = ComputeSegmentBytes(bytesRx, duration, intervalStartUtcMs, overlapStart, overlapEnd);
        var tx = ComputeSegmentBytes(bytesTx, duration, intervalStartUtcMs, overlapStart, overlapEnd);

        return (rx, tx);
    }

    /// <summary>
    /// Computes allocated bytes for a sub-range [overlapStart, overlapEnd) using cumulative monotonic mapping.
    /// Uses Int128 arithmetic to eliminate 64-bit multiplication overflow.
    /// </summary>
    private static long ComputeSegmentBytes(
        long totalBytes,
        long intervalDuration,
        long intervalStart,
        long overlapStart,
        long overlapEnd)
    {
        if (totalBytes == 0) return 0L;

        var cumStart = CumulativeBytes(totalBytes, intervalDuration, intervalStart, overlapStart);
        var cumEnd = CumulativeBytes(totalBytes, intervalDuration, intervalStart, overlapEnd);

        var segment = cumEnd - cumStart;
        return Math.Max(0L, segment);
    }

    /// <summary>
    /// Returns CumulativeBytes(t) = floor((Int128)totalBytes * (t - intervalStart) / intervalDuration).
    /// </summary>
    public static long CumulativeBytes(
        long totalBytes,
        long intervalDuration,
        long intervalStart,
        long t)
    {
        if (t <= intervalStart) return 0L;
        var intervalEnd = intervalStart + intervalDuration;
        if (t >= intervalEnd) return totalBytes;

        var elapsed = (Int128)(t - intervalStart);
        var numerator = (Int128)totalBytes * elapsed;
        var allocated = (long)(numerator / intervalDuration);

        return Math.Clamp(allocated, 0L, totalBytes);
    }
}
