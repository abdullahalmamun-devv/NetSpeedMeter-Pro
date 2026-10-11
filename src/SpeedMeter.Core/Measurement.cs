using System.Globalization;

namespace SpeedMeter.Core;

/// <summary>A provider-issued counter lifetime. Empty means the provider cannot identify lifetimes.</summary>
public readonly record struct CounterEpoch(Guid Value);

public sealed record AdapterCounter(string Id, string Name, string Description,
    bool IsUp, ulong Received, ulong Sent, ulong LinkSpeed, bool IsHardware = false,
    CounterEpoch SourceEpoch = default);

public enum ReadingState { Measuring, Live, Unavailable, Paused, Discontinuity }

public sealed record Reading(DateTimeOffset Timestamp, string? ScopeId, ReadingState State,
    double? Download, double? Upload, ulong Received, ulong Sent,
    double CoveredSeconds, bool Coarse, string Detail)
{
    public Guid SnapshotId { get; init; }
    public Guid SessionId { get; init; }
    /// <summary>Engine-local baseline generation; compare together with SessionId.</summary>
    public ulong MeasurementEpoch { get; init; }
    public CounterEpoch SourceEpoch { get; init; }
    /// <summary>Sample time in the collector's monotonic clock domain, never wall-clock time.</summary>
    public double? MonotonicSeconds { get; init; }
    public ulong? IntervalBytesRx { get; init; }
    public ulong? IntervalBytesTx { get; init; }
    public double? IntervalDurationSeconds { get; init; }
    public bool IsValid => State == ReadingState.Live;
}

/// <summary>Consumes one explicit interface boundary. UI rates never feed accounting.</summary>
public sealed class MeasurementEngine
{
    private AdapterCounter? previous;
    private double previousTime;
    private ulong received;
    private ulong sent;
    private double coveredSeconds;
    private string? scope;
    private Guid sessionId = Guid.NewGuid();
    private ulong measurementEpoch;
    private CounterEpoch sourceEpoch;

    public void SelectScope(string? id)
    {
        if (scope == id) return;
        scope = id;
        ResetSession();
    }

    /// <summary>Rebaselines on a canonical scope revision without erasing previous cumulative session totals.</summary>
    public void RebaselineForScopeChange(string? newScopeId)
    {
        if (scope == newScopeId) return;
        scope = newScopeId;
        previous = null;
        sourceEpoch = default;
        measurementEpoch = checked(measurementEpoch + 1);
    }

    public void ResetSession()
    {
        previous = null;
        received = sent = 0;
        coveredSeconds = 0;
        sessionId = Guid.NewGuid();
        sourceEpoch = default;
    }

    public Reading Interrupt(DateTimeOffset utc, ReadingState state, string detail, double? monotonicSeconds = null)
    {
        if (state == ReadingState.Live || !Enum.IsDefined(state))
            throw new ArgumentException("An interruption cannot be a valid live observation.", nameof(state));
        previous = null;
        return Make(utc, state, null, null, false, detail, monotonicSeconds);
    }

    public Reading Sample(AdapterCounter? current, double monotonicSeconds,
        DateTimeOffset utc, double expectedInterval)
    {
        if (current is null || current.Id != scope || !current.IsUp)
            return Interrupt(utc, ReadingState.Unavailable,
                scope is null ? "Choose an adapter to begin." : "Selected adapter unavailable. No replacement selected.", monotonicSeconds);
        if (!double.IsFinite(monotonicSeconds) || expectedInterval <= 0 || !double.IsFinite(expectedInterval))
            return Interrupt(utc, ReadingState.Discontinuity, "Invalid sampling clock; waiting for a new baseline.", monotonicSeconds);

        var baseline = previous;
        var elapsed = monotonicSeconds - previousTime;
        if (baseline is null)
        {
            CaptureBaseline(current, monotonicSeconds);
            return Make(utc, ReadingState.Measuring, null, null, false, "Baseline captured. Waiting for the next sample.", monotonicSeconds);
        }
        if (current.SourceEpoch != baseline.SourceEpoch || !double.IsFinite(elapsed) || elapsed <= 0 ||
            current.Received < baseline.Received || current.Sent < baseline.Sent)
        {
            CaptureBaseline(current, monotonicSeconds);
            return Make(utc, ReadingState.Discontinuity, null, null, false, "Counter or clock discontinuity. New baseline captured.", monotonicSeconds);
        }

        var rx = current.Received - baseline.Received;
        var tx = current.Sent - baseline.Sent;
        var download = rx / elapsed;
        var upload = tx / elapsed;
        var nextCoveredSeconds = coveredSeconds + elapsed;
        if (!double.IsFinite(download) || !double.IsFinite(upload) || !double.IsFinite(nextCoveredSeconds))
        {
            CaptureBaseline(current, monotonicSeconds);
            return Make(utc, ReadingState.Discontinuity, null, null, false, "Sampling arithmetic exceeded capacity. New baseline captured.", monotonicSeconds);
        }
        // Calculate both totals before assigning either, so an overflow is atomic.
        try
        {
            var nextReceived = checked(received + rx);
            var nextSent = checked(sent + tx);
            received = nextReceived;
            sent = nextSent;
        }
        catch (OverflowException)
        {
            CaptureBaseline(current, monotonicSeconds);
            return Make(utc, ReadingState.Discontinuity, null, null, false, "Session counter capacity exceeded. Reset the session.", monotonicSeconds);
        }
        previous = current;
        previousTime = monotonicSeconds;
        coveredSeconds = nextCoveredSeconds;
        var coarse = elapsed / expectedInterval > 3;
        return Make(utc, ReadingState.Live, download, upload, coarse,
            coarse ? "Delayed sample: bytes retained; timing is coarse." : "Live adapter traffic · includes local network traffic",
            monotonicSeconds, rx, tx, elapsed);
    }

    private void CaptureBaseline(AdapterCounter current, double monotonicSeconds)
    {
        previous = current;
        previousTime = monotonicSeconds;
        sourceEpoch = current.SourceEpoch;
        measurementEpoch = checked(measurementEpoch + 1);
    }

    private Reading Make(DateTimeOffset utc, ReadingState state, double? rx, double? tx, bool coarse, string detail,
        double? monotonicSeconds = null, ulong? intervalRx = null, ulong? intervalTx = null, double? intervalDuration = null)
        => new(utc, scope, state, rx, tx, received, sent, coveredSeconds, coarse, detail)
        {
            SnapshotId = Guid.NewGuid(), SessionId = sessionId,
            MeasurementEpoch = measurementEpoch, SourceEpoch = sourceEpoch,
            MonotonicSeconds = monotonicSeconds is double seconds && double.IsFinite(seconds) ? seconds : null,
            IntervalBytesRx = intervalRx,
            IntervalBytesTx = intervalTx,
            IntervalDurationSeconds = intervalDuration
        };
}

public static class MetricFormat
{
    public static string Rate(double? bytesPerSecond, bool bits = false)
    {
        if (bytesPerSecond is null || !double.IsFinite(bytesPerSecond.Value) || bytesPerSecond.Value < 0) return "—";
        var value = bytesPerSecond.Value * (bits ? 8 : 1);
        if (!double.IsFinite(value)) return "—";
        string[] units = bits ? ["bps", "Kbps", "Mbps", "Gbps", "Tbps"] : ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];
        var index = 0;
        while (value >= 1000 && index < units.Length - 1) { value /= 1000; index++; }
        return value.ToString(index >= 2 ? "0.00" : "0.#", CultureInfo.InvariantCulture) + " " + units[index];
    }

    public static string Bytes(ulong bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var index = 0;
        while (value >= 1000 && index < units.Length - 1) { value /= 1000; index++; }
        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[index];
    }

    public static string Bytes(long bytes) => Bytes((ulong)Math.Max(0L, bytes));
}
