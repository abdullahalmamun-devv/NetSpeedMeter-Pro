using System.Diagnostics;

namespace SpeedMeter.Application;

public readonly record struct ClockReading(double MonotonicSeconds, DateTimeOffset UtcNow);

public interface IMeasurementClock
{
    ClockReading Read();
}

public sealed class MeasurementClock : IMeasurementClock
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    public ClockReading Read() => new(elapsed.Elapsed.TotalSeconds, DateTimeOffset.UtcNow);
}
