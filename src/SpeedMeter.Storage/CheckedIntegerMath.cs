using System;

namespace SpeedMeter.Storage;

public class StorageCapacityExceededException : Exception
{
    public StorageCapacityExceededException(string message) : base(message) { }
    public StorageCapacityExceededException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Enforces checked 64-bit integer range invariants before persisting into SQLite signed INTEGER columns.
/// </summary>
public static class CheckedIntegerMath
{
    public static long CheckedToSigned(ulong delta, string context)
    {
        if (delta > (ulong)long.MaxValue)
        {
            throw new StorageCapacityExceededException(
                $"Interval delta for {context} exceeds signed 64-bit integer capacity ({delta} > {long.MaxValue}).");
        }
        return (long)delta;
    }

    public static long CheckedAdd(long a, long b, string context)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException ex)
        {
            throw new StorageCapacityExceededException($"Aggregation overflow for {context}: {a} + {b} exceeds capacity.", ex);
        }
    }
}
