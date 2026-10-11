using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

public static class V1Wave2Fixtures
{
    private static readonly DateTimeOffset SampleUtc = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubClock : IMeasurementClock
    {
        public double CurrentMonotonic { get; set; } = 100.0;
        public DateTimeOffset CurrentUtc { get; set; } = SampleUtc;
        public ClockReading Read() => new(CurrentMonotonic, CurrentUtc);
    }

    private sealed class StubCounterProvider : IAdapterCounterProvider
    {
        public List<AdapterCounter> Adapters { get; set; } = [];
        public ProviderAvailability Availability { get; set; } = ProviderAvailability.Available;
        public AdapterObservation ReadSnapshot() => new(Availability, Adapters, "stub", "1.0", "Stub observation");
    }

    public static void Run(Action<string, Action> check)
    {
        // 1. Integer Splitter & Conservation
        check("W2-SPLIT: IntegerSplitter partition conservation property across arbitrary boundaries", TestIntegerSplitter_PartitionConservationProperty);
        check("W2-SPLIT: IntegerSplitter exact remainder allocation and boundary matching", TestIntegerSplitter_ExactRemainderAllocationAndBoundary);
        check("W2-SPLIT: IntegerSplitter Int128 arithmetic eliminates 64-bit multiplication overflow", TestIntegerSplitter_Extreme64BitValuesDoNotOverflow);

        // 2. Calendar Resolver & Timezones
        check("W2-CALENDAR: CalendarRangeResolver Today Yesterday Week Month boundaries", TestCalendarResolver_TodayYesterdayWeekMonth);
        check("W2-CALENDAR: Billing cycle day 31 clamps in February without mutating requested cycle day", TestCalendarResolver_BillingCycleClampingPreservesRequestedDay);
        check("W2-CALENDAR: Billing cycle February leap year (29) vs common year (28) clamping", TestCalendarResolver_LeapYearVsCommonYearFebruary);
        check("W2-CALENDAR: DST spring forward (23h) and fall back (25h) local day boundary resolution", TestCalendarResolver_DstSpringForwardAndFallBack);
        check("W2-CALENDAR: Fixed-offset non-DST timezone (Asia/Dhaka) sanity resolution", TestCalendarResolver_NonDstSanityAsiaDhaka);

        // 3. Storage Historical Summaries & Coverage
        check("W2-HIST: No double counting across adjacent half-open ranges (A + B == C)", () => TestStorage_UsageSummaryNoDoubleCountAcrossAdjacentRanges().GetAwaiter().GetResult());
        check("W2-HIST: UsageSummary tracks observed and missing duration with typed CompletenessState", () => TestStorage_UsageSummaryCoverageAndCompletenessStates().GetAwaiter().GetResult());
        check("W2-HIST: History availability boundary distinguishes unrecorded history from missing gaps", () => TestStorage_HistoryAvailabilityPreInstallationUnavailable().GetAwaiter().GetResult());
        check("W2-HIST: Keyset cursor pagination over intervals provides stable deterministic traversal", () => TestStorage_KeysetPagedIntervalsConsistency().GetAwaiter().GetResult());
        check("W2-HIST: Daily rollup generation is idempotent and reconciles exactly with raw intervals", () => TestStorage_DailyRollupIdempotencyAndRawReconciliation().GetAwaiter().GetResult());

        // 4. Billing & Quota Engine
        check("W2-QUOTA: QuotaEvaluator calculates remaining bytes and overage beyond 100% accurately", TestQuota_EvaluationAndOverageBeyond100);
        check("W2-QUOTA: Opening balance improves effective quota calculation without inflating coverage", TestQuota_OpeningBalanceSeparationDoesNotFalsifyCoverage);
        check("W2-QUOTA: Warning threshold crossing emits in deterministic order and handles threshold jumping", TestQuota_ThresholdCrossingAndSkipping);
        check("W2-QUOTA: Quota threshold deduplication persists in SQLite and survives storage restart", () => TestQuota_DeduplicationPersistsAcrossStorageRestart().GetAwaiter().GetResult());

        // 5. Live Graph Buffer
        check("W2-GRAPH: LiveGraphBuffer memory is strictly bounded and downsampling preserves peak spikes", TestLiveGraphBuffer_BoundedMemoryAndPeakPreservingDownsampling);

        // 6. Settings Foundation
        check("W2-SETTINGS: SettingsStorage persists atomically with versioned schema and sanitization", TestSettings_AtomicWriteAndSchemaVersioning);
        check("W2-SETTINGS: SettingsStorage corrupt file recovery creates backup and loads safe defaults", TestSettings_CorruptionRecoveryPreservesBackupAndSafeDefaults);

        // 7. Concurrent Collection + Historical Query
        check("W2-CONCURRENCY: Active background Collector persistence runs alongside concurrent queries without stall", () => TestConcurrentCollectionAndHistoricalQueries().GetAwaiter().GetResult());
    }

    public static void TestIntegerSplitter_PartitionConservationProperty()
    {
        // An interval from 1000 to 5000 (duration = 4000ms), 100,000 bytes Rx, 50,000 bytes Tx
        var start = 1000L;
        var end = 5000L;
        var totalRx = 100_000L;
        var totalTx = 50_000L;

        // Arbitrary partition boundaries: 0, 1500, 2200, 3800, 5000, 6000
        long[] partitions = [0, 1500, 2200, 3800, 5000, 6000];

        long sumRx = 0;
        long sumTx = 0;

        for (var i = 0; i < partitions.Length - 1; i++)
        {
            var pStart = partitions[i];
            var pEnd = partitions[i + 1];
            var (allocRx, allocTx) = IntegerSplitter.Allocate(start, end, totalRx, totalTx, pStart, pEnd);
            sumRx += allocRx;
            sumTx += allocTx;
        }

        Assert(sumRx == totalRx, $"Partition conservation failed for RX: sum={sumRx}, original={totalRx}");
        Assert(sumTx == totalTx, $"Partition conservation failed for TX: sum={sumTx}, original={totalTx}");
    }

    public static void TestIntegerSplitter_ExactRemainderAllocationAndBoundary()
    {
        // Interval: [0, 10), bytes = 7. Cut at 5.
        // Left [0, 5): 7 * 5 / 10 = 3
        // Right [5, 10): 7 - 3 = 4
        var (leftRx, leftTx) = IntegerSplitter.Allocate(0, 10, 7, 7, 0, 5);
        var (rightRx, rightTx) = IntegerSplitter.Allocate(0, 10, 7, 7, 5, 10);

        Assert(leftRx == 3, $"Left RX allocation mismatch: expected 3, got {leftRx}");
        Assert(rightRx == 4, $"Right RX allocation mismatch: expected 4, got {rightRx}");
        Assert(leftRx + rightRx == 7, "Remainder was lost or doubled across boundary");

        // Single byte total: bytes = 1, interval [0, 10), cut at 5
        // Cumulative(5) = floor(1 * 5 / 10) = 0
        // Left [0, 5): 0. Right [5, 10): 1 - 0 = 1.
        var (b1Left, _) = IntegerSplitter.Allocate(0, 10, 1, 0, 0, 5);
        var (b1Right, _) = IntegerSplitter.Allocate(0, 10, 1, 0, 5, 10);
        Assert(b1Left == 0 && b1Right == 1, "Single byte division failed exact remainder rule");

        // Disjoint range outside interval returns 0
        var (outRx, outTx) = IntegerSplitter.Allocate(0, 10, 100, 100, 20, 30);
        Assert(outRx == 0 && outTx == 0, "Disjoint range must allocate 0 bytes");

        // Zero duration interval returns 0 without division by zero
        var (zeroRx, zeroTx) = IntegerSplitter.Allocate(10, 10, 100, 100, 0, 20);
        Assert(zeroRx == 0 && zeroTx == 0, "Zero duration interval must allocate 0 bytes");
    }

    public static void TestIntegerSplitter_Extreme64BitValuesDoNotOverflow()
    {
        // Interval with huge byte count near long.MaxValue: 9,000,000,000,000,000,000
        var hugeBytes = 9_000_000_000_000_000_000L;
        var start = 1_000_000L;
        var end = 2_000_000L; // duration = 1,000,000ms

        // Cut exactly in half: [1_000_000, 1_500_000) and [1_500_000, 2_000_000)
        var (firstHalf, _) = IntegerSplitter.Allocate(start, end, hugeBytes, 0, start, 1_500_000L);
        var (secondHalf, _) = IntegerSplitter.Allocate(start, end, hugeBytes, 0, 1_500_000L, end);

        Assert(firstHalf == 4_500_000_000_000_000_000L, $"Huge first half allocation mismatch: {firstHalf}");
        Assert(secondHalf == 4_500_000_000_000_000_000L, $"Huge second half allocation mismatch: {secondHalf}");
        Assert(firstHalf + secondHalf == hugeBytes, "Huge integer sum must equal original total");
    }

    public static void TestCalendarResolver_TodayYesterdayWeekMonth()
    {
        var tz = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 10, 15, 14, 30, 0, TimeSpan.Zero);

        // Today
        var today = CalendarRangeResolver.GetToday(tz, now);
        var expTodayStart = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expTodayEnd = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(today.StartUtcMs == expTodayStart && today.EndUtcMs == expTodayEnd, "Today boundary mismatch");

        // Yesterday
        var yesterday = CalendarRangeResolver.GetYesterday(tz, now);
        var expYestStart = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(yesterday.StartUtcMs == expYestStart && yesterday.EndUtcMs == expTodayStart, "Yesterday boundary mismatch");

        // This Week (starting Monday: 2026-10-15 is Thursday -> Monday is 2026-10-12)
        var week = CalendarRangeResolver.GetThisWeek(tz, now, DayOfWeek.Monday);
        var expWeekStart = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expWeekEnd = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(week.StartUtcMs == expWeekStart && week.EndUtcMs == expWeekEnd, "Week boundary mismatch");

        // This Month
        var month = CalendarRangeResolver.GetThisMonth(tz, now);
        var expMonthStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expMonthEnd = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(month.StartUtcMs == expMonthStart && month.EndUtcMs == expMonthEnd, "Month boundary mismatch");
    }

    public static void TestCalendarResolver_BillingCycleClampingPreservesRequestedDay()
    {
        var tz = TimeZoneInfo.Utc;

        // Requested cycle day: 31
        // Mid February 2026 (common year, 28 days)
        var febNow = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        var febCycle = CalendarRangeResolver.GetBillingCycle(tz, febNow, 31);

        // In Feb 15, previous month was Jan (31 days). So start is Jan 31.
        // Current month is Feb (clamped to 28). So end is Feb 28.
        var expJan31 = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expFeb28 = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(febCycle.StartUtcMs == expJan31 && febCycle.EndUtcMs == expFeb28, "Feb billing cycle clamp mismatch");

        // Now in early March 2026: March 5
        var marNow = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
        var marCycle = CalendarRangeResolver.GetBillingCycle(tz, marNow, 31);

        // Previous month was Feb (28 days). Start is Feb 28.
        // Current month is March (31 days). Clamping releases back to requested day 31!
        var expMar31 = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(marCycle.StartUtcMs == expFeb28 && marCycle.EndUtcMs == expMar31,
            "Cycle day 31 must release clamping in March and NOT stay stuck at 28");
    }

    public static void TestCalendarResolver_LeapYearVsCommonYearFebruary()
    {
        var tz = TimeZoneInfo.Utc;

        // 2024 is a leap year (February has 29 days)
        var leapFeb = new DateTimeOffset(2024, 2, 10, 12, 0, 0, TimeSpan.Zero);
        var leapCycle = CalendarRangeResolver.GetBillingCycle(tz, leapFeb, 31);

        var expJan31 = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expFeb29 = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(leapCycle.StartUtcMs == expJan31 && leapCycle.EndUtcMs == expFeb29,
            "Leap year February 2024 must clamp to Feb 29");
    }

    public static void TestCalendarResolver_DstSpringForwardAndFallBack()
    {
        // Eastern Standard Time: US Eastern has DST
        TimeZoneInfo estTz;
        try { estTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch { return; /* Skip if Windows TZ not present on non-Windows environment */ }

        // Spring forward: 2026-03-08 (local day is 23 hours)
        var springNow = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.FromHours(-4));
        var springRange = CalendarRangeResolver.GetToday(estTz, springNow);
        var springHours = (springRange.EndUtcMs - springRange.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(springHours - 23.0) < 0.1, $"Spring forward local day should be 23 hours, was {springHours}");

        // Fall back: 2026-11-01 (local day is 25 hours)
        var fallNow = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5));
        var fallRange = CalendarRangeResolver.GetToday(estTz, fallNow);
        var fallHours = (fallRange.EndUtcMs - fallRange.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(fallHours - 25.0) < 0.1, $"Fall back local day should be 25 hours, was {fallHours}");
    }

    public static void TestCalendarResolver_NonDstSanityAsiaDhaka()
    {
        // Asia/Dhaka or Bangladesh Standard Time (fixed UTC+6, no DST)
        TimeZoneInfo bstTz;
        try { bstTz = TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time"); }
        catch
        {
            try { bstTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"); }
            catch { return; }
        }

        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(6));
        var today = CalendarRangeResolver.GetToday(bstTz, now);
        var todayHours = (today.EndUtcMs - today.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(todayHours - 24.0) < 0.001, "Fixed offset timezone must always resolve exactly 24.0 hour days");
    }

    public static async Task TestStorage_UsageSummaryNoDoubleCountAcrossAdjacentRanges()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_sum_no_double_{Guid.NewGuid():N}.db");
        var sid = "sess_summary_test";
        var clk = "clk_summary_test";
        var rev = "rev_summary_test";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 0));

            // Interval 1: [0, 5000), 50 MB Rx
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv1", "key1", sid, clk, rev, "unidentified", 0, 5000, 50_000_000, 50_000_000, 5_000_000, false, false, null, "nominal"));

            // Interval 2: [5000, 10000), 100 MB Rx
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv2", "key2", sid, clk, rev, "unidentified", 5000, 10000, 50_000_000, 100_000_000, 10_000_000, false, false, null, "nominal"));

            // Interval 3: [10000, 15000), 75 MB Rx (crosses mid-point at 12000)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv3", "key3", sid, clk, rev, "unidentified", 10000, 15000, 50_000_000, 75_000_000, 7_500_000, false, false, null, "nominal"));

            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Query A: [0, 12000)
            // Query B: [12000, 20000)
            // Query C: [0, 20000)
            var rangeA = new HistoricalRange(0, 12000);
            var rangeB = new HistoricalRange(12000, 20000);
            var rangeC = new HistoricalRange(0, 20000);

            var sumA = await storage.GetUsageSummaryAsync(rangeA, "UTC");
            var sumB = await storage.GetUsageSummaryAsync(rangeB, "UTC");
            var sumC = await storage.GetUsageSummaryAsync(rangeC, "UTC");

            // Partition conservation: A.Bytes + B.Bytes == C.Bytes
            Assert(sumA.BytesRx + sumB.BytesRx == sumC.BytesRx,
                $"RX double count or loss detected: A({sumA.BytesRx}) + B({sumB.BytesRx}) != C({sumC.BytesRx})");
            Assert(sumA.BytesTx + sumB.BytesTx == sumC.BytesTx,
                $"TX double count or loss detected: A({sumA.BytesTx}) + B({sumB.BytesTx}) != C({sumC.BytesTx})");

            // Exact total: 50M + 100M + 75M = 225M
            Assert(sumC.BytesRx == 225_000_000, $"Total RX mismatch: expected 225,000,000, got {sumC.BytesRx}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_UsageSummaryCoverageAndCompletenessStates()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_coverage_{Guid.NewGuid():N}.db");
        var sid = "sess_cov_test";
        var clk = "clk_cov_test";
        var rev = "rev_cov_test";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 1000, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 1000, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 1000));

            // Valid interval: [1000, 5000) (4 seconds valid)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv1", "k1", sid, clk, rev, "unidentified", 1000, 5000, 40_000_000, 10_000, 1_000, false, false, null, "nominal"));

            // Explicit Gap: [5000, 7000) (2 seconds unobserved gap)
            await storage.IngestGapAsync(new UsageIntervalRecord(
                "gap1", "k2", sid, clk, rev, "unidentified", 5000, 7000, 20_000_000, 0, 0, false, true, "system_sleep", "nominal"));

            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Range: [1000, 7000) (total 6000ms: 4000ms valid, 2000ms gap)
            var range = new HistoricalRange(1000, 7000);
            var sum = await storage.GetUsageSummaryAsync(range, "UTC");

            Assert(sum.ObservedDurationMs == 4000, $"Observed duration mismatch: expected 4000, got {sum.ObservedDurationMs}");
            Assert(sum.MissingDurationMs == 2000, $"Missing duration mismatch: expected 2000, got {sum.MissingDurationMs}");
            Assert(Math.Abs(sum.CoverageRatio - (4.0 / 6.0)) < 0.001, $"Coverage ratio mismatch: {sum.CoverageRatio}");
            Assert(sum.Completeness == CompletenessState.Partial, "Must be Partial completeness when gaps exist");

            // Range: [1000, 5000) (100% complete)
            var completeRange = new HistoricalRange(1000, 5000);
            var completeSum = await storage.GetUsageSummaryAsync(completeRange, "UTC");
            Assert(completeSum.Completeness == CompletenessState.Complete, "Must be Complete when 0 gaps exist in range");
            Assert(completeSum.CoverageRatio == 1.0, "Complete range coverage must be 1.0");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_HistoryAvailabilityPreInstallationUnavailable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_unavail_{Guid.NewGuid():N}.db");
        var sid = "sess_avail_test";
        var clk = "clk_avail_test";
        var rev = "rev_avail_test";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 10000, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 10000, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 10000));

            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv1", "k1", sid, clk, rev, "unidentified", 10000, 15000, 50_000_000, 1000, 100, false, false, null, "nominal"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var (earliest, latest) = await storage.GetHistoryAvailabilityAsync();
            Assert(earliest == 10000 && latest == 15000, "Availability boundary mismatch");

            // Query for range strictly BEFORE installation: [0, 5000)
            var preInstallRange = new HistoricalRange(0, 5000);
            var preSum = await storage.GetUsageSummaryAsync(preInstallRange, "UTC");
            Assert(preSum.Completeness == CompletenessState.Unavailable,
                "Range before earliest history must be flagged Unavailable, NOT fake zero coverage");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_KeysetPagedIntervalsConsistency()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_paging_{Guid.NewGuid():N}.db");
        var sid = "sess_page_test";
        var clk = "clk_page_test";
        var rev = "rev_page_test";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 0));

            // Insert 15 intervals
            for (var i = 0; i < 15; i++)
            {
                await storage.IngestIntervalAsync(new UsageIntervalRecord(
                    $"iv_{i:D3}", $"key_{i}", sid, clk, rev, "unidentified",
                    i * 1000, (i + 1) * 1000, 10_000_000, 1000 * (i + 1), 100 * (i + 1), false, false, null, "nominal"));
            }
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Page through with pageSize = 5
            var range = new HistoricalRange(0, 15000);
            var page1 = await storage.GetPagedIntervalsAsync(range, null, null, pageSize: 5);
            Assert(page1.Count == 5, $"Page 1 count mismatch: {page1.Count}");
            Assert(page1[0].IntervalId == "iv_000" && page1[4].IntervalId == "iv_004", "Page 1 order mismatch");

            var page2 = await storage.GetPagedIntervalsAsync(range, page1[4].StartUtcMs, page1[4].IntervalId, pageSize: 5);
            Assert(page2.Count == 5, $"Page 2 count mismatch: {page2.Count}");
            Assert(page2[0].IntervalId == "iv_005" && page2[4].IntervalId == "iv_009", "Page 2 order mismatch");

            var page3 = await storage.GetPagedIntervalsAsync(range, page2[4].StartUtcMs, page2[4].IntervalId, pageSize: 5);
            Assert(page3.Count == 5, $"Page 3 count mismatch: {page3.Count}");
            Assert(page3[0].IntervalId == "iv_010" && page3[4].IntervalId == "iv_014", "Page 3 order mismatch");

            // Next page is empty
            var page4 = await storage.GetPagedIntervalsAsync(range, page3[4].StartUtcMs, page3[4].IntervalId, pageSize: 5);
            Assert(page4.Count == 0, "Page 4 should be empty at end of results");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_DailyRollupIdempotencyAndRawReconciliation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_rollup_{Guid.NewGuid():N}.db");
        var sid = "sess_rollup_test";
        var clk = "clk_rollup_test";
        var rev = "rev_rollup_test";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 0));

            // Write 3 intervals in Day 1 (UTC timestamp around 2026-10-11)
            var baseDay1 = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            await storage.IngestIntervalAsync(new UsageIntervalRecord("r1", "k1", sid, clk, rev, "unidentified", baseDay1, baseDay1 + 1000, 10_000_000, 10_000_000, 1_000_000, false, false, null, "nominal"));
            await storage.IngestIntervalAsync(new UsageIntervalRecord("r2", "k2", sid, clk, rev, "unidentified", baseDay1 + 1000, baseDay1 + 2000, 10_000_000, 20_000_000, 2_000_000, false, false, null, "nominal"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var range = new HistoricalRange(baseDay1 - 3600_000, baseDay1 + 7200_000);

            // Compute rollups once
            var count1 = await storage.ComputeDailyRollupsAsync(range, "UTC");
            Assert(count1 > 0, "Must compute at least 1 rollup");

            // Recompute rollups (idempotency test: should replace, not duplicate)
            var count2 = await storage.ComputeDailyRollupsAsync(range, "UTC");
            Assert(count2 == count1, "Repeated rollup generation must be idempotent");

            // Verify in SQLite that total rows in daily_rollups == 1
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), SUM(bytes_rx), SUM(bytes_tx) FROM daily_rollups;";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert(await reader.ReadAsync(), "Must read rollup count");
            Assert(reader.GetInt64(0) == 1, $"Duplicate rollups created: expected 1 row, got {reader.GetInt64(0)}");

            // Reconciliation test: rollup bytes must equal raw sum exactly
            var rollupRx = reader.GetInt64(1);
            var rollupTx = reader.GetInt64(2);
            var rawSummary = await storage.GetUsageSummaryAsync(range, "UTC");
            Assert(rollupRx == rawSummary.BytesRx, $"Rollup RX ({rollupRx}) != Raw RX ({rawSummary.BytesRx})");
            Assert(rollupTx == rawSummary.BytesTx, $"Rollup TX ({rollupTx}) != Raw TX ({rawSummary.BytesTx})");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static void TestQuota_EvaluationAndOverageBeyond100()
    {
        var rule = new QuotaRule(
            "rule-1", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 100_000_000L, [0.80, 0.90, 1.00], "UTC");

        // 50% usage
        var eval50 = QuotaEvaluator.Evaluate(rule, 30_000_000, 20_000_000, []);
        Assert(!eval50.IsExceeded, "50% should not be exceeded");
        Assert(eval50.RemainingBytes == 50_000_000, $"Remaining mismatch: {eval50.RemainingBytes}");
        Assert(eval50.CrossedThresholds.Count == 0, "No thresholds should be crossed at 50%");

        // 125% overage usage
        var eval125 = QuotaEvaluator.Evaluate(rule, 75_000_000, 50_000_000, []);
        Assert(eval125.IsExceeded, "125% must be exceeded");
        Assert(eval125.RemainingBytes == 0, "Remaining must be 0 when exceeded");
        Assert(Math.Abs(eval125.UsedRatio - 1.25) < 0.001, "Used ratio must accurately reflect overage (1.25)");
        Assert(eval125.CrossedThresholds.Count == 3, "All 3 thresholds crossed at 125%");
    }

    public static void TestQuota_OpeningBalanceSeparationDoesNotFalsifyCoverage()
    {
        var rule = new QuotaRule(
            "rule-2", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 100_000_000L, [0.80, 1.00], "UTC",
            OpeningBalanceBytes: 50_000_000L);

        // Recorded is 40 MB. Opening balance is 50 MB.
        var eval = QuotaEvaluator.Evaluate(rule, 30_000_000, 10_000_000, []);

        Assert(eval.RecordedBytes == 40_000_000, "Recorded bytes must remain pure and separated");
        Assert(eval.OpeningBalanceBytes == 50_000_000, "Opening balance must remain separated");
        Assert(eval.EffectiveUsedBytes == 90_000_000, "Effective usage must be 90 MB");
        Assert(eval.CrossedThresholds.Contains(0.80), "80% threshold crossed by effective usage");
    }

    public static void TestQuota_ThresholdCrossingAndSkipping()
    {
        var rule = new QuotaRule(
            "rule-3", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 100_000_000L, [0.80, 0.90, 1.00], "UTC");

        // Usage jumps from 79 MB to 95 MB in one step
        // Previously fired: none []
        var eval = QuotaEvaluator.Evaluate(rule, 95_000_000, 0, []);

        // Both 80% and 90% must be emitted deterministically in newlyCrossed
        Assert(eval.NewlyCrossedThresholds.Count == 2, $"Expected 2 newly crossed thresholds, got {eval.NewlyCrossedThresholds.Count}");
        Assert(eval.NewlyCrossedThresholds[0] == 0.80, "First newly crossed threshold must be 0.80");
        Assert(eval.NewlyCrossedThresholds[1] == 0.90, "Second newly crossed threshold must be 0.90");

        // If evaluated again with [0.80, 0.90] marked as already fired
        var evalNext = QuotaEvaluator.Evaluate(rule, 96_000_000, 0, [0.80, 0.90]);
        Assert(evalNext.NewlyCrossedThresholds.Count == 0, "Already fired thresholds must be deduplicated");
    }

    public static async Task TestQuota_DeduplicationPersistsAcrossStorageRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_quota_dedup_{Guid.NewGuid():N}.db");
        var ruleId = "quota_rule_dedup";
        var cycleId = "2026-10-01";

        try
        {
            // Session 1: Record 80% threshold event
            await using (var storage1 = new SqliteStorageEngine(dbPath))
            {
                var evt = new QuotaThresholdEvent(
                    Guid.NewGuid().ToString("D"), ruleId, 1, cycleId, 0.80, 10000, 80_000_000);
                await storage1.RecordQuotaThresholdEventAsync(evt);
                await storage1.FlushAsync(TimeSpan.FromSeconds(5));
            }

            // Session 2: Reopen storage simulating restart
            await using (var storage2 = new SqliteStorageEngine(dbPath))
            {
                var recorded = await storage2.GetRecordedThresholdsAsync(ruleId, 1, cycleId);
                Assert(recorded.Count == 1, $"Expected 1 recorded threshold, got {recorded.Count}");
                Assert(recorded[0] == 0.80, "Recorded threshold must be 0.80");

                // Evaluate quota: since 0.80 was already recorded, it must NOT be newly emitted
                var rule = new QuotaRule(
                    ruleId, 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
                    QuotaDirectionMode.Combined, 100_000_000L, [0.80, 0.90], "UTC");

                var eval = QuotaEvaluator.Evaluate(rule, 85_000_000, 0, recorded);
                Assert(eval.CrossedThresholds.Contains(0.80), "80% is crossed");
                Assert(!eval.NewlyCrossedThresholds.Contains(0.80), "80% must NOT be emitted again after restart");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static void TestLiveGraphBuffer_BoundedMemoryAndPeakPreservingDownsampling()
    {
        var buffer = new LiveGraphBuffer(capacitySeconds: 100);

        // Append 500 points in a loop: buffer must never grow beyond capacity of 100
        for (var i = 0; i < 500; i++)
        {
            buffer.Append(new LiveGraphPoint(1000 * i, 1000, 500, true, false, i));
        }
        Assert(buffer.Count == 100, $"Buffer count should be clamped to capacity 100, was {buffer.Count}");

        // Test peak-preserving downsampling
        var spikeBuffer = new LiveGraphBuffer(capacitySeconds: 60);
        var baseTime = 100_000L;

        for (var i = 0; i < 60; i++)
        {
            // Inject a sharp spike at t = 30s: 50,000,000 B/s; others are 1,000 B/s
            var rx = (i == 30) ? 50_000_000.0 : 1_000.0;
            spikeBuffer.Append(new LiveGraphPoint(baseTime + (i * 1000), rx, 500.0, true, false, i));
        }

        // Downsample 60 seconds into 6 buckets (10 seconds each)
        var downsampled = spikeBuffer.GetDownsampled(TimeSpan.FromSeconds(60), 6, baseTime + 60_000);
        Assert(downsampled.Count == 6, $"Expected 6 downsampled buckets, got {downsampled.Count}");

        // Bucket containing the spike (t = 30 falls in bucket 3) must preserve the 50 MB/s peak!
        var peakObserved = downsampled.Max(b => b.PeakDownloadRate);
        Assert(peakObserved == 50_000_000.0, $"Peak downsampling lost the spike! Max observed: {peakObserved}");
    }

    public static void TestSettings_AtomicWriteAndSchemaVersioning()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"speedmeter_settings_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SpeedMeterSettings
            {
                DisplayUnits = "MB/s",
                DecimalPrecision = 3,
                SampleIntervalSeconds = 2.0,
                WidgetOpacity = 0.85,
                ReportingTimezoneId = "UTC"
            };

            SettingsStorage.SaveAtomic(tempFile, settings);
            Assert(File.Exists(tempFile), "Settings file was not created atomically");

            var (loaded, warn) = SettingsStorage.Load(tempFile);
            Assert(warn == null, $"Unexpected load warning: {warn}");
            Assert(loaded.SchemaVersion == 1, "Schema version mismatch");
            Assert(loaded.DisplayUnits == "MB/s", "Display units mismatch");
            Assert(loaded.DecimalPrecision == 3, "Decimal precision mismatch");
            Assert(loaded.SampleIntervalSeconds == 2.0, "Sample interval mismatch");
            Assert(loaded.WidgetOpacity == 0.85, "Widget opacity mismatch");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public static void TestSettings_CorruptionRecoveryPreservesBackupAndSafeDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"speedmeter_settings_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            // Write completely malformed JSON
            File.WriteAllText(tempFile, "{ broken json corrupt content !!! ");

            var (loaded, warn) = SettingsStorage.Load(tempFile);
            Assert(warn != null && warn.Contains("corrupted"), "Must surface corruption warning");
            Assert(loaded != null, "Must return safe default settings");
            Assert(loaded!.DisplayUnits == "Mbps", "Default display units must be Mbps");
            Assert(loaded.WidgetOpacity == 0.90, "Default opacity must be 0.90");

            // Verify corrupted backup file was created
            var dir = Path.GetDirectoryName(tempFile)!;
            var pattern = Path.GetFileName(tempFile) + ".corrupt.*";
            var backups = Directory.GetFiles(dir, pattern);
            Assert(backups.Length > 0, "Corrupted settings file backup was not preserved");

            foreach (var b in backups) { try { File.Delete(b); } catch { } }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public static async Task TestConcurrentCollectionAndHistoricalQueries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_concur_{Guid.NewGuid():N}.db");
        var clock = new StubClock { CurrentMonotonic = 10.0, CurrentUtc = SampleUtc };
        var counterProvider = new StubCounterProvider
        {
            Adapters = [new AdapterCounter("adapter-concur", "Eth", "NIC", true, 100, 100, 1000, true)]
        };

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            using var collector = new Collector(counterProvider, clock, storage: storage);
            collector.Select("adapter-concur");
            collector.Start();

            // Run active collection while issuing 25 concurrent historical queries
            var queryTasks = new List<Task>();
            for (var i = 0; i < 25; i++)
            {
                // Advance clock and counter
                clock.CurrentMonotonic += 1.0;
                clock.CurrentUtc = clock.CurrentUtc.AddSeconds(1.0);
                counterProvider.Adapters = [new AdapterCounter("adapter-concur", "Eth", "NIC", true, (ulong)(100 + (i * 1000)), 100, 1000, true)];
                collector.SampleOnce();

                var range = new HistoricalRange(SampleUtc.ToUnixTimeMilliseconds(), clock.CurrentUtc.ToUnixTimeMilliseconds() + 1000);
                queryTasks.Add(Task.Run(async () =>
                {
                    var sum = await storage.GetUsageSummaryAsync(range, "UTC");
                    Assert(sum != null, "Historical summary must not be null");
                    var paged = await storage.GetPagedIntervalsAsync(range, pageSize: 10);
                    Assert(paged != null, "Paged intervals must not be null");
                }));
            }

            await Task.WhenAll(queryTasks);
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            Assert(storage.Status.Health == StorageHealth.Healthy,
                $"Storage should remain Healthy during concurrent queries, was: {storage.Status.Health} ({storage.Status.DegradedReason})");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    private static void DeleteDbFiles(string dbPath)
    {
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        try { if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal"); } catch { }
        try { if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm"); } catch { }
    }
}
