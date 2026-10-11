using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpeedMeter.Application;

/// <param name="AwakeSeconds">Optional Windows awake-time counter. Unlike Stopwatch,
/// this counter excludes time spent in sleep or hibernation.</param>
public readonly record struct ClockReading(
    double MonotonicSeconds, DateTimeOffset UtcNow, double? AwakeSeconds = null);

public interface IMeasurementClock
{
    ClockReading Read();
}

public sealed class MeasurementClock : IMeasurementClock
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private static bool preciseAwakeClockAvailable = true;

    public ClockReading Read()
    {
        var monotonic = elapsed.Elapsed.TotalSeconds;
        var utc = DateTimeOffset.UtcNow;
        return new ClockReading(monotonic, utc, ReadAwakeSeconds());
    }

    private static double? ReadAwakeSeconds()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (preciseAwakeClockAvailable)
        {
            try
            {
                QueryUnbiasedInterruptTimePrecise(out var preciseTicks);
                return preciseTicks / 10_000_000.0;
            }
            catch (EntryPointNotFoundException)
            {
                preciseAwakeClockAvailable = false;
            }
            catch (DllNotFoundException)
            {
                preciseAwakeClockAvailable = false;
            }
        }

        // Some supported Windows builds expose only the tick-resolution API.
        // Its 10-16 ms resolution is sufficient for the collector's 1 s sleep guard.
        try
        {
            return QueryUnbiasedInterruptTime(out var ticks) ? ticks / 10_000_000.0 : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void QueryUnbiasedInterruptTimePrecise(out ulong unbiasedInterruptTimePrecise);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
