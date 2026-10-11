using System;
using System.Collections.Generic;

namespace SpeedMeter.Core;

/// <summary>
/// A single data point in the live in-memory telemetry buffer.
/// Distinguishes between observed idle (0 B/s, valid) and unobserved gap (null rate, invalid).
/// </summary>
public readonly record struct LiveGraphPoint(
    long TimestampUtcMs,
    double? DownloadRate,
    double? UploadRate,
    bool IsValid,
    bool IsGap,
    long Sequence);

/// <summary>
/// Summarized bucket point resulting from peak-preserving downsampling.
/// </summary>
public readonly record struct DownsampledPoint(
    long TimestampUtcMs,
    double? PeakDownloadRate,
    double? PeakUploadRate,
    double? AvgDownloadRate,
    double? AvgUploadRate,
    bool HasData,
    bool HasGap);

/// <summary>
/// Pre-allocated circular ring buffer for live in-memory graph history.
/// Memory usage is strictly bounded indefinitely.
/// </summary>
public sealed class LiveGraphBuffer
{
    private readonly LiveGraphPoint[] buffer;
    private readonly int capacity;
    private readonly object gate = new();
    private int head;
    private int count;

    public int Capacity => capacity;
    public int Count { get { lock (gate) return count; } }

    public LiveGraphBuffer(int capacitySeconds = 3600)
    {
        if (capacitySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySeconds), "Capacity must be positive.");
        this.capacity = capacitySeconds;
        this.buffer = new LiveGraphPoint[capacity];
    }

    /// <summary>
    /// Appends a new point to the circular ring buffer.
    /// Overwrites the oldest item when full, keeping memory allocation strictly zero during continuous sampling.
    /// Rejects out-of-order timestamps and deterministically updates duplicate timestamps.
    /// </summary>
    public void Append(LiveGraphPoint point)
    {
        lock (gate)
        {
            if (count > 0)
            {
                var lastIdx = (head - 1 + capacity) % capacity;
                var lastPoint = buffer[lastIdx];

                // Monotonic guard: Reject out-of-order points to protect chronology
                if (point.TimestampUtcMs < lastPoint.TimestampUtcMs)
                {
                    return;
                }

                // Deterministic duplicate timestamp resolution: update in place if higher or equal sequence
                if (point.TimestampUtcMs == lastPoint.TimestampUtcMs)
                {
                    if (point.Sequence >= lastPoint.Sequence)
                    {
                        buffer[lastIdx] = point;
                    }
                    return;
                }
            }

            buffer[head] = point;
            head = (head + 1) % capacity;
            if (count < capacity) count++;
        }
    }

    /// <summary>
    /// Extracts a snapshot of points for the specified window duration ending at nowUtcMs.
    /// </summary>
    public IReadOnlyList<LiveGraphPoint> GetPoints(TimeSpan window, long nowUtcMs)
    {
        var windowMs = (long)window.TotalMilliseconds;
        var cutoff = nowUtcMs - windowMs;

        lock (gate)
        {
            var list = new List<LiveGraphPoint>(Math.Min(count, (int)window.TotalSeconds + 1));
            var startIdx = (head - count + capacity) % capacity;

            for (var i = 0; i < count; i++)
            {
                var idx = (startIdx + i) % capacity;
                var pt = buffer[idx];
                if (pt.TimestampUtcMs >= cutoff)
                {
                    list.Add(pt);
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Performs peak-preserving downsampling over the requested time window.
    /// Divides the window into bucketCount slices and selects peak rates to avoid smoothing away transient spikes.
    /// </summary>
    public IReadOnlyList<DownsampledPoint> GetDownsampled(TimeSpan window, int bucketCount, long nowUtcMs)
    {
        if (bucketCount <= 0) throw new ArgumentOutOfRangeException(nameof(bucketCount), "Bucket count must be positive.");

        var points = GetPoints(window, nowUtcMs);
        var windowMs = (long)window.TotalMilliseconds;
        var startMs = nowUtcMs - windowMs;
        var bucketDurationMs = (double)windowMs / bucketCount;

        var result = new DownsampledPoint[bucketCount];

        for (var b = 0; b < bucketCount; b++)
        {
            var bStart = startMs + (long)(b * bucketDurationMs);
            var bEnd = startMs + (long)((b + 1) * bucketDurationMs);
            var midTime = (bStart + bEnd) / 2;

            double? peakRx = null;
            double? peakTx = null;
            double sumRx = 0;
            double sumTx = 0;
            var validSamples = 0;
            var hasGap = false;

            foreach (var pt in points)
            {
                if (pt.TimestampUtcMs >= bStart && pt.TimestampUtcMs < bEnd)
                {
                    if (pt.IsGap || !pt.IsValid)
                    {
                        hasGap = true;
                    }
                    if (pt.IsValid && pt.DownloadRate.HasValue && pt.UploadRate.HasValue)
                    {
                        validSamples++;
                        sumRx += pt.DownloadRate.Value;
                        sumTx += pt.UploadRate.Value;
                        peakRx = peakRx.HasValue ? Math.Max(peakRx.Value, pt.DownloadRate.Value) : pt.DownloadRate.Value;
                        peakTx = peakTx.HasValue ? Math.Max(peakTx.Value, pt.UploadRate.Value) : pt.UploadRate.Value;
                    }
                }
            }

            if (validSamples > 0)
            {
                result[b] = new DownsampledPoint(
                    midTime,
                    peakRx,
                    peakTx,
                    sumRx / validSamples,
                    sumTx / validSamples,
                    true,
                    hasGap);
            }
            else
            {
                result[b] = new DownsampledPoint(
                    midTime,
                    null,
                    null,
                    null,
                    null,
                    false,
                    hasGap);
            }
        }

        return result;
    }
}
