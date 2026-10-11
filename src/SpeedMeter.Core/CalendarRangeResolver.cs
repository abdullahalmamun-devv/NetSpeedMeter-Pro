using System;

namespace SpeedMeter.Core;

/// <summary>
/// Timezone-aware calendar range resolution for historical queries and billing cycles.
/// All output ranges are in UTC milliseconds with half-open [StartUtcMs, EndUtcMs) semantics.
/// </summary>
public static class CalendarRangeResolver
{
    /// <summary>
    /// Resolves a requested QueryPeriodType into exact UTC milliseconds range boundaries.
    /// </summary>
    public static HistoricalRange ResolveRange(
        QueryPeriodType periodType,
        TimeZoneInfo timezone,
        DateTimeOffset nowUtc,
        int cycleDay = 1,
        DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        ArgumentNullException.ThrowIfNull(timezone);

        return periodType switch
        {
            QueryPeriodType.Today => GetToday(timezone, nowUtc),
            QueryPeriodType.Yesterday => GetYesterday(timezone, nowUtc),
            QueryPeriodType.ThisWeek => GetThisWeek(timezone, nowUtc, startOfWeek),
            QueryPeriodType.ThisMonth => GetThisMonth(timezone, nowUtc),
            QueryPeriodType.BillingCycle => GetBillingCycle(timezone, nowUtc, cycleDay),
            _ => throw new ArgumentException($"Period type {periodType} requires explicit custom boundaries.", nameof(periodType))
        };
    }

    /// <summary>Resolves "Today" in the specified reporting timezone.</summary>
    public static HistoricalRange GetToday(TimeZoneInfo timezone, DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
        var startLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);

        var startUtc = ToUtcOffset(startLocal, timezone);
        var endUtc = ToUtcOffset(endLocal, timezone);

        return new HistoricalRange(startUtc.ToUnixTimeMilliseconds(), endUtc.ToUnixTimeMilliseconds());
    }

    /// <summary>Resolves "Yesterday" in the specified reporting timezone.</summary>
    public static HistoricalRange GetYesterday(TimeZoneInfo timezone, DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
        var todayLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var yesterdayLocal = todayLocal.AddDays(-1);

        var startUtc = ToUtcOffset(yesterdayLocal, timezone);
        var endUtc = ToUtcOffset(todayLocal, timezone);

        return new HistoricalRange(startUtc.ToUnixTimeMilliseconds(), endUtc.ToUnixTimeMilliseconds());
    }

    /// <summary>Resolves "This Week" in the specified reporting timezone given a week start day.</summary>
    public static HistoricalRange GetThisWeek(TimeZoneInfo timezone, DateTimeOffset nowUtc, DayOfWeek startOfWeek = DayOfWeek.Monday)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
        var todayLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);

        var diff = ((int)todayLocal.DayOfWeek - (int)startOfWeek + 7) % 7;
        var startLocal = todayLocal.AddDays(-diff);
        var endLocal = startLocal.AddDays(7);

        var startUtc = ToUtcOffset(startLocal, timezone);
        var endUtc = ToUtcOffset(endLocal, timezone);

        return new HistoricalRange(startUtc.ToUnixTimeMilliseconds(), endUtc.ToUnixTimeMilliseconds());
    }

    /// <summary>Resolves "This Month" in the specified reporting timezone.</summary>
    public static HistoricalRange GetThisMonth(TimeZoneInfo timezone, DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
        var startLocal = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddMonths(1);

        var startUtc = ToUtcOffset(startLocal, timezone);
        var endUtc = ToUtcOffset(endLocal, timezone);

        return new HistoricalRange(startUtc.ToUnixTimeMilliseconds(), endUtc.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Resolves the current monthly billing cycle for a requested cycle day (1..31).
    /// Clamps months with fewer days (e.g. February 28/29 or April 30) without mutating the requested cycle day.
    /// </summary>
    public static HistoricalRange GetBillingCycle(TimeZoneInfo timezone, DateTimeOffset nowUtc, int cycleDay)
    {
        if (cycleDay < 1 || cycleDay > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleDay), cycleDay, "Requested cycle day must be between 1 and 31.");
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
        var currentYear = localNow.Year;
        var currentMonth = localNow.Month;
        var currentDay = localNow.Day;

        var effectiveDayInCurrentMonth = Math.Min(cycleDay, DateTime.DaysInMonth(currentYear, currentMonth));

        DateTime startLocal;
        DateTime endLocal;

        if (currentDay >= effectiveDayInCurrentMonth)
        {
            // Current billing cycle started in current month, ends in next month
            startLocal = new DateTime(currentYear, currentMonth, effectiveDayInCurrentMonth, 0, 0, 0, DateTimeKind.Unspecified);

            var nextMonthDate = new DateTime(currentYear, currentMonth, 1).AddMonths(1);
            var nextMonthDays = DateTime.DaysInMonth(nextMonthDate.Year, nextMonthDate.Month);
            var effectiveDayInNextMonth = Math.Min(cycleDay, nextMonthDays);

            endLocal = new DateTime(nextMonthDate.Year, nextMonthDate.Month, effectiveDayInNextMonth, 0, 0, 0, DateTimeKind.Unspecified);
        }
        else
        {
            // Current billing cycle started in previous month, ends in current month
            var prevMonthDate = new DateTime(currentYear, currentMonth, 1).AddMonths(-1);
            var prevMonthDays = DateTime.DaysInMonth(prevMonthDate.Year, prevMonthDate.Month);
            var effectiveDayInPrevMonth = Math.Min(cycleDay, prevMonthDays);

            startLocal = new DateTime(prevMonthDate.Year, prevMonthDate.Month, effectiveDayInPrevMonth, 0, 0, 0, DateTimeKind.Unspecified);
            endLocal = new DateTime(currentYear, currentMonth, effectiveDayInCurrentMonth, 0, 0, 0, DateTimeKind.Unspecified);
        }

        var startUtc = ToUtcOffset(startLocal, timezone);
        var endUtc = ToUtcOffset(endLocal, timezone);

        return new HistoricalRange(startUtc.ToUnixTimeMilliseconds(), endUtc.ToUnixTimeMilliseconds());
    }

    private static DateTimeOffset ToUtcOffset(DateTime unspecifiedLocal, TimeZoneInfo tz)
    {
        if (tz.IsInvalidTime(unspecifiedLocal))
        {
            // During DST spring forward, the clock jumps forward over this time.
            // Move forward by the DST adjustment gap to resolve to valid instant.
            var adjustment = tz.GetAdjustmentRules();
            var gap = TimeSpan.FromHours(1);
            return new DateTimeOffset(unspecifiedLocal + gap, tz.GetUtcOffset(unspecifiedLocal + gap)).ToUniversalTime();
        }

        if (tz.IsAmbiguousTime(unspecifiedLocal))
        {
            // During DST fall back, the clock repeats an hour.
            // Prefer standard time offset to provide deterministic resolution.
            var offsets = tz.GetAmbiguousTimeOffsets(unspecifiedLocal);
            var standardOffset = offsets[0] < offsets[1] ? offsets[0] : offsets[1];
            return new DateTimeOffset(unspecifiedLocal, standardOffset).ToUniversalTime();
        }

        var offset = tz.GetUtcOffset(unspecifiedLocal);
        return new DateTimeOffset(unspecifiedLocal, offset).ToUniversalTime();
    }
}
