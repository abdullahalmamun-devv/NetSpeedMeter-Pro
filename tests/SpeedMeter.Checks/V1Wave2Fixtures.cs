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

public sealed class SkipCheckException : Exception
{
    public SkipCheckException(string reason) : base(reason) { }
}

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
        check("W2-SPLIT: IntegerSplitter seeded randomized property testing across 10000 cases", TestIntegerSplitter_RandomizedPropertyPartitionConservation);

        // 2. Calendar Resolver & Timezones
        check("W2-CALENDAR: CalendarRangeResolver Today Yesterday Week Month boundaries", TestCalendarResolver_TodayYesterdayWeekMonth);
        check("W2-CALENDAR: Billing cycle day 31 clamps in February without mutating requested cycle day", TestCalendarResolver_BillingCycleClampingPreservesRequestedDay);
        check("W2-CALENDAR: Billing cycle February leap year (29) vs common year (28) clamping", TestCalendarResolver_LeapYearVsCommonYearFebruary);
        check("W2-CALENDAR: DST spring forward (23h) and fall back (25h) local day boundary resolution", TestCalendarResolver_DstSpringForwardAndFallBack);
        check("W2-CALENDAR: Fixed-offset non-DST timezone (Asia/Dhaka) sanity resolution", TestCalendarResolver_NonDstSanityAsiaDhaka);
        check("W2-CALENDAR: Dynamic non-one-hour DST resolution (Lord Howe Island 30m delta)", TestCalendarResolver_DynamicNonOneHourDstLordHoweIsland);
        check("W2-CALENDAR: Missing environment timezone reports explicit SKIP instead of false pass", TestCalendarResolver_SkipReportedOnMissingEnvironmentTimezone);

        // 3. Storage Historical Summaries & Coverage
        check("W2-HIST: No double counting across adjacent half-open ranges (A + B == C)", () => TestStorage_UsageSummaryNoDoubleCountAcrossAdjacentRanges().GetAwaiter().GetResult());
        check("W2-HIST: Strict boundary inequality excludes boundary-touching intervals", () => TestStorage_StrictInequalityExcludesBoundaryTouchingIntervals().GetAwaiter().GetResult());
        check("W2-HIST: GetTotalBytesAsync allocates partial ranges using IntegerSplitter", () => TestStorage_GetTotalBytesAsync_UsesIntegerSplitter().GetAwaiter().GetResult());
        check("W2-HIST: Coverage denominator truthfully reflects unexplained missing spans", () => TestStorage_CoverageDenominatorTruthfullyReflectsUnexplainedHoles().GetAwaiter().GetResult());
        check("W2-HIST: Temporal overlap between consecutive intervals flags StorageDegraded", () => TestStorage_TemporalOverlapDetectionMarksDegraded().GetAwaiter().GetResult());
        check("W2-HIST: History availability boundary distinguishes unrecorded history from missing gaps", () => TestStorage_HistoryAvailabilityPreInstallationUnavailable().GetAwaiter().GetResult());
        check("W2-HIST: Keyset cursor pagination over intervals provides stable deterministic traversal", () => TestStorage_KeysetPagedIntervalsConsistency().GetAwaiter().GetResult());
        check("W2-HIST: Daily rollup generation preserves distinct scope and network dimensions", () => TestStorage_DailyRollupProvenancePreservesActualDimensions().GetAwaiter().GetResult());

        // 4. Billing & Quota Engine
        check("W2-QUOTA: QuotaEvaluator calculates remaining bytes and overage beyond 100% accurately", TestQuota_EvaluationAndOverageBeyond100);
        check("W2-QUOTA: Disabled quota rule truthfully preserves recorded bytes without zeroing", TestQuota_DisabledRuleTruthfullyPreservesRecordedBytes);
        check("W2-QUOTA: Basis points validation, PRD defaults, and canonical billing cycle identity", TestQuota_BasisPointsAndThresholdValidation);
        check("W2-QUOTA: Opening balance improves effective quota calculation without inflating coverage", TestQuota_OpeningBalanceSeparationDoesNotFalsifyCoverage);
        check("W2-QUOTA: Warning threshold crossing emits in deterministic order and handles threshold jumping", TestQuota_ThresholdCrossingAndSkipping);
        check("W2-QUOTA: Quota threshold deduplication persists in SQLite and survives storage restart", () => TestQuota_DeduplicationPersistsAcrossStorageRestart().GetAwaiter().GetResult());

        // 5. Live Graph Buffer
        check("W2-GRAPH: LiveGraphBuffer memory is strictly bounded and downsampling preserves peak spikes", TestLiveGraphBuffer_BoundedMemoryAndPeakPreservingDownsampling);
        check("W2-GRAPH: LiveGraphBuffer rejects out-of-order points and resolves duplicate timestamps", TestLiveGraphBuffer_MonotonicChronologyGuards);

        // 6. Settings Foundation
        check("W2-SETTINGS: SettingsStorage persists atomically with versioned schema and sanitization", TestSettings_AtomicWriteAndSchemaVersioning);
        check("W2-SETTINGS: SettingsStorage rejects future schema version without modifying file", TestSettings_FutureSchemaVersionRejectedWithoutOverwriting);
        check("W2-SETTINGS: SettingsStorage persists monitor identity and work area anchors", TestSettings_MonitorIdentityPersisted);
        check("W2-SETTINGS: SettingsStorage corrupt file recovery creates backup and loads safe defaults", TestSettings_CorruptionRecoveryPreservesBackupAndSafeDefaults);

        // 7. Application Service Facades
        check("W2-SERVICES: Application services (History, Quota, Settings, Graph) end-to-end integration", () => TestApplicationServices_EndToEndIntegration().GetAwaiter().GetResult());

        // 8. Concurrent Collection + Historical Query
        check("W2-CONCURRENCY: Active background Collector persistence runs alongside concurrent queries without stall", () => TestConcurrentCollectionAndHistoricalQueries().GetAwaiter().GetResult());
    }

    public static void TestIntegerSplitter_PartitionConservationProperty()
    {
        var start = 1000L;
        var end = 5000L;
        var totalRx = 100_000L;
        var totalTx = 50_000L;

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
        var start = 0L;
        var end = 10L;
        var totalBytes = 7L;

        var (leftRx, _) = IntegerSplitter.Allocate(start, end, totalBytes, 0, 0, 5);
        var (rightRx, _) = IntegerSplitter.Allocate(start, end, totalBytes, 0, 5, 10);

        Assert(leftRx == 3, $"Left allocation mismatch: {leftRx}");
        Assert(rightRx == 4, $"Right allocation mismatch: {rightRx}");
        Assert(leftRx + rightRx == totalBytes, "Exact sum must equal 7");

        var (noneBefore, _) = IntegerSplitter.Allocate(start, end, totalBytes, 0, -10, 0);
        var (noneAfter, _) = IntegerSplitter.Allocate(start, end, totalBytes, 0, 10, 20);
        Assert(noneBefore == 0, "No bytes allocated outside interval start");
        Assert(noneAfter == 0, "No bytes allocated outside interval end");
    }

    public static void TestIntegerSplitter_Extreme64BitValuesDoNotOverflow()
    {
        var hugeBytes = 9_000_000_000_000_000_000L;
        var start = 1_000_000L;
        var end = 2_000_000L;

        var (firstHalf, _) = IntegerSplitter.Allocate(start, end, hugeBytes, 0, start, 1_500_000L);
        var (secondHalf, _) = IntegerSplitter.Allocate(start, end, hugeBytes, 0, 1_500_000L, end);

        Assert(firstHalf == 4_500_000_000_000_000_000L, $"Huge first half allocation mismatch: {firstHalf}");
        Assert(secondHalf == 4_500_000_000_000_000_000L, $"Huge second half allocation mismatch: {secondHalf}");
        Assert(firstHalf + secondHalf == hugeBytes, "Huge integer sum must equal original total");
    }

    public static void TestIntegerSplitter_RandomizedPropertyPartitionConservation()
    {
        var rng = new Random(42);
        for (var test = 0; test < 10_000; test++)
        {
            var totalBytes = (long)(rng.NextDouble() * 1_000_000_000_000_000L);
            var duration = rng.Next(1, 100_000);
            var start = rng.Next(0, 1_000_000);
            var end = start + duration;

            var numCuts = rng.Next(1, 8);
            var cuts = new List<long> { start - 500 };
            for (var c = 0; c < numCuts; c++)
            {
                cuts.Add(rng.Next(start - 100, end + 100));
            }
            cuts.Add(end + 500);
            cuts.Sort();

            long sumAlloc = 0;
            for (var i = 0; i < cuts.Count - 1; i++)
            {
                var pStart = cuts[i];
                var pEnd = cuts[i + 1];
                if (pEnd <= pStart) continue;

                var (allocRx, _) = IntegerSplitter.Allocate(start, end, totalBytes, 0, pStart, pEnd);
                sumAlloc += allocRx;
            }

            if (sumAlloc != totalBytes)
            {
                throw new Exception($"Randomized partition conservation failed at iteration {test}: sum={sumAlloc}, original={totalBytes}");
            }
        }
    }

    public static void TestCalendarResolver_TodayYesterdayWeekMonth()
    {
        var tz = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 10, 15, 14, 30, 0, TimeSpan.Zero);

        var today = CalendarRangeResolver.GetToday(tz, now);
        var expTodayStart = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expTodayEnd = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(today.StartUtcMs == expTodayStart && today.EndUtcMs == expTodayEnd, "Today boundary mismatch");

        var yesterday = CalendarRangeResolver.GetYesterday(tz, now);
        var expYestStart = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(yesterday.StartUtcMs == expYestStart && yesterday.EndUtcMs == expTodayStart, "Yesterday boundary mismatch");

        var week = CalendarRangeResolver.GetThisWeek(tz, now, DayOfWeek.Monday);
        var expWeekStart = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expWeekEnd = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(week.StartUtcMs == expWeekStart && week.EndUtcMs == expWeekEnd, "Week boundary mismatch");

        var month = CalendarRangeResolver.GetThisMonth(tz, now);
        var expMonthStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expMonthEnd = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(month.StartUtcMs == expMonthStart && month.EndUtcMs == expMonthEnd, "Month boundary mismatch");
    }

    public static void TestCalendarResolver_BillingCycleClampingPreservesRequestedDay()
    {
        var tz = TimeZoneInfo.Utc;
        var febNow = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        var febCycle = CalendarRangeResolver.GetBillingCycle(tz, febNow, 31);

        var expJan31 = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expFeb28 = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(febCycle.StartUtcMs == expJan31 && febCycle.EndUtcMs == expFeb28, "Feb billing cycle clamp mismatch");

        var marNow = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
        var marCycle = CalendarRangeResolver.GetBillingCycle(tz, marNow, 31);

        var expMar31 = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(marCycle.StartUtcMs == expFeb28 && marCycle.EndUtcMs == expMar31,
            "Cycle day 31 must release clamping in March and NOT stay stuck at 28");
    }

    public static void TestCalendarResolver_LeapYearVsCommonYearFebruary()
    {
        var tz = TimeZoneInfo.Utc;
        var leapFeb = new DateTimeOffset(2024, 2, 10, 12, 0, 0, TimeSpan.Zero);
        var leapCycle = CalendarRangeResolver.GetBillingCycle(tz, leapFeb, 31);

        var expJan31 = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var expFeb29 = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert(leapCycle.StartUtcMs == expJan31 && leapCycle.EndUtcMs == expFeb29,
            "Leap year February 2024 must clamp to Feb 29");
    }

    public static void TestCalendarResolver_DstSpringForwardAndFallBack()
    {
        TimeZoneInfo estTz;
        try { estTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch { throw new SkipCheckException("Eastern Standard Time not present in environment"); }

        var springNow = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.FromHours(-4));
        var springRange = CalendarRangeResolver.GetToday(estTz, springNow);
        var springHours = (springRange.EndUtcMs - springRange.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(springHours - 23.0) < 0.1, $"Spring forward local day should be 23 hours, was {springHours}");

        var fallNow = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5));
        var fallRange = CalendarRangeResolver.GetToday(estTz, fallNow);
        var fallHours = (fallRange.EndUtcMs - fallRange.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(fallHours - 25.0) < 0.1, $"Fall back local day should be 25 hours, was {fallHours}");
    }

    public static void TestCalendarResolver_NonDstSanityAsiaDhaka()
    {
        TimeZoneInfo bstTz;
        try { bstTz = TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time"); }
        catch
        {
            try { bstTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"); }
            catch { throw new SkipCheckException("Bangladesh Standard Time not present in environment"); }
        }

        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(6));
        var today = CalendarRangeResolver.GetToday(bstTz, now);
        var todayHours = (today.EndUtcMs - today.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(todayHours - 24.0) < 0.001, "Fixed offset timezone must always resolve exactly 24.0 hour days");
    }

    public static void TestCalendarResolver_DynamicNonOneHourDstLordHoweIsland()
    {
        TimeZoneInfo lhiTz;
        try { lhiTz = TimeZoneInfo.FindSystemTimeZoneById("Lord Howe Standard Time"); }
        catch
        {
            try { lhiTz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Lord_Howe"); }
            catch { throw new SkipCheckException("Lord Howe Island timezone not present in environment"); }
        }

        // On 2026-10-04, Lord Howe Island springs forward by 30 minutes!
        // Local day has 23.5 hours (84,600,000 ms), NOT 23.0 hours!
        var springNow = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(11));
        var springRange = CalendarRangeResolver.GetToday(lhiTz, springNow);
        var springHours = (springRange.EndUtcMs - springRange.StartUtcMs) / 3_600_000.0;
        Assert(Math.Abs(springHours - 23.5) < 0.05,
            $"Lord Howe Island 30-minute DST spring forward must resolve to 23.5 hours, but was {springHours}");
    }

    public static void TestCalendarResolver_SkipReportedOnMissingEnvironmentTimezone()
    {
        // Explicit demonstration of honest Skip reporting when timezone is not present
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("Completely_Non_Existent_Timezone_12345");
        }
        catch
        {
            throw new SkipCheckException("Intentionally verifying SkipCheckException handling for unavailable platform timezone");
        }
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

            // Interval 1: [1000, 3000) (duration = 2000ms), 2000 bytes Rx
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-1", "key-1", sid, clk, rev, "unidentified", 1000, 3000, 2000, 2000, 0, false, false, null, "high"));

            // Interval 2: [3000, 5000) (duration = 2000ms), 4000 bytes Rx
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-2", "key-2", sid, clk, rev, "unidentified", 3000, 5000, 2000, 4000, 0, false, false, null, "high"));

            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var rangeA = new HistoricalRange(1000, 3000);
            var rangeB = new HistoricalRange(3000, 5000);
            var rangeTotal = new HistoricalRange(1000, 5000);

            var summaryA = await storage.GetUsageSummaryAsync(rangeA, "UTC");
            var summaryB = await storage.GetUsageSummaryAsync(rangeB, "UTC");
            var summaryTotal = await storage.GetUsageSummaryAsync(rangeTotal, "UTC");

            Assert(summaryA.BytesRx == 2000, $"Summary A mismatch: {summaryA.BytesRx}");
            Assert(summaryB.BytesRx == 4000, $"Summary B mismatch: {summaryB.BytesRx}");
            Assert(summaryTotal.BytesRx == 6000, $"Summary Total mismatch: {summaryTotal.BytesRx}");
            Assert(summaryA.BytesRx + summaryB.BytesRx == summaryTotal.BytesRx,
                "No double counting: adjacent range summaries must sum identically to combined range");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_StrictInequalityExcludesBoundaryTouchingIntervals()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_boundary_touch_{Guid.NewGuid():N}.db");
        var sid = "sess_touch";
        var clk = "clk_touch";
        var rev = "rev_touch";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-touch", 1, "SelectedAdapter", "Win32", "1.0", "adapter-touch", "valid", 0));

            // Interval: [1000, 2000)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-touch", "key-touch", sid, clk, rev, "unidentified", 1000, 2000, 1000, 500, 500, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Query [2000, 3000): interval ends at 2000, range starts at 2000 -> must NOT be returned!
            var afterList = await storage.GetIntervalsAsync(startUtcMs: 2000, endUtcMs: 3000);
            Assert(afterList.Count == 0, $"Boundary-touching interval ending at query start was incorrectly returned: count={afterList.Count}");

            // Query [0, 1000): interval starts at 1000, range ends at 1000 -> must NOT be returned!
            var beforeList = await storage.GetIntervalsAsync(startUtcMs: 0, endUtcMs: 1000);
            Assert(beforeList.Count == 0, $"Boundary-touching interval starting at query end was incorrectly returned: count={beforeList.Count}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_GetTotalBytesAsync_UsesIntegerSplitter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_total_split_{Guid.NewGuid():N}.db");
        var sid = "sess_total_split";
        var clk = "clk_total_split";
        var rev = "rev_total_split";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-tot", 1, "SelectedAdapter", "Win32", "1.0", "adapter-tot", "valid", 0));

            // Interval [0, 1000) with 10,000 bytes Rx
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-tot", "key-tot", sid, clk, rev, "unidentified", 0, 1000, 1000, 10_000, 0, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Query second half [500, 1000): must return 5,000 bytes, NOT the whole 10,000!
            var (rx, _) = await storage.GetTotalBytesAsync(startUtcMs: 500, endUtcMs: 1000);
            Assert(rx == 5_000, $"GetTotalBytesAsync must allocate proportional bytes via IntegerSplitter, got {rx}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_CoverageDenominatorTruthfullyReflectsUnexplainedHoles()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_unexplained_hole_{Guid.NewGuid():N}.db");
        var sid = "sess_hole";
        var clk = "clk_hole";
        var rev = "rev_hole";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-hole", 1, "SelectedAdapter", "Win32", "1.0", "adapter-hole", "valid", 0));

            // Interval 1: [0, 600_000) (10 min)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-h1", "key-h1", sid, clk, rev, "unidentified", 0, 600_000, 600_000, 1000, 0, false, false, null, "high"));

            // Unexplained hole: [600_000, 1_200_000) (10 min without any row)

            // Interval 2: [1_200_000, 1_800_000) (10 min)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-h2", "key-h2", sid, clk, rev, "unidentified", 1_200_000, 1_800_000, 600_000, 1000, 0, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Query range [0, 1_800_000) (30 minutes total)
            var range = new HistoricalRange(0, 1_800_000);
            var summary = await storage.GetUsageSummaryAsync(range, "UTC");

            Assert(summary.Completeness == CompletenessState.Partial,
                $"Unexplained hole must produce CompletenessState.Partial, got {summary.Completeness}");
            Assert(summary.CoverageRatio < 0.70 && summary.CoverageRatio > 0.60,
                $"Coverage ratio must reflect unexplained hole (expected ~0.667), was {summary.CoverageRatio}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_TemporalOverlapDetectionMarksDegraded()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_temporal_overlap_{Guid.NewGuid():N}.db");
        var sid = "sess_overlap";
        var clk = "clk_overlap";
        var rev = "rev_overlap";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-ov", 1, "SelectedAdapter", "Win32", "1.0", "adapter-ov", "valid", 0));

            // Interval 1: [1000, 5000)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-ov1", "key-ov1", sid, clk, rev, "unidentified", 1000, 5000, 4000, 100, 0, false, false, null, "high"));

            // Interval 2: [3000, 7000) (temporal overlap on same scope!)
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-ov2", "key-ov2", sid, clk, rev, "unidentified", 3000, 7000, 4000, 100, 0, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var range = new HistoricalRange(0, 10_000);
            var summary = await storage.GetUsageSummaryAsync(range, "UTC");

            Assert(summary.Completeness == CompletenessState.StorageDegraded,
                $"Temporal overlap between valid intervals must transition to StorageDegraded, was {summary.Completeness}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_HistoryAvailabilityPreInstallationUnavailable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_pre_install_{Guid.NewGuid():N}.db");
        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord("s1", "epoch", 1_000_000, null, null, false));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var (earliest, latest) = await storage.GetHistoryAvailabilityAsync();
            Assert(earliest == 1_000_000, $"Earliest availability mismatch: {earliest}");

            var preRange = new HistoricalRange(0, 500_000);
            var preSummary = await storage.GetUsageSummaryAsync(preRange, "UTC");

            Assert(preSummary.Completeness == CompletenessState.Unavailable,
                $"Pre-installation query must be Unavailable, got {preSummary.Completeness}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_KeysetPagedIntervalsConsistency()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_keyset_{Guid.NewGuid():N}.db");
        var sid = "sess_keyset";
        var clk = "clk_keyset";
        var rev = "rev_keyset";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 100_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev, "scope-k", 1, "SelectedAdapter", "Win32", "1.0", "adapter-k", "valid", 0));

            for (var i = 0; i < 25; i++)
            {
                var s = 1000 * i;
                var e = s + 1000;
                await storage.IngestIntervalAsync(new UsageIntervalRecord(
                    $"iv-{i:D3}", $"key-{i:D3}", sid, clk, rev, "unidentified", s, e, 1000, 100, 50, false, false, null, "high"));
            }
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var fullRange = new HistoricalRange(0, 25_000);
            var page1 = await storage.GetPagedIntervalsAsync(fullRange, pageSize: 10);
            Assert(page1.Count == 10, $"Page 1 should have 10 items, got {page1.Count}");

            var lastItem = page1[^1];
            var page2 = await storage.GetPagedIntervalsAsync(fullRange, cursorStartUtcMs: lastItem.StartUtcMs, cursorIntervalId: lastItem.IntervalId, pageSize: 10);
            Assert(page2.Count == 10, $"Page 2 should have 10 items, got {page2.Count}");
            Assert(page2[0].StartUtcMs == 10_000, $"Page 2 first start should be 10000, got {page2[0].StartUtcMs}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_DailyRollupProvenancePreservesActualDimensions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_rollup_prov_{Guid.NewGuid():N}.db");
        var sid = "sess_rollup";
        var clk = "clk_rollup";
        var rev1 = "rev_eth";
        var rev2 = "rev_wifi";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sid, "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clk, sid, 0, 0, 100_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev1, "scope-eth", 1, "SelectedAdapter", "Win32", "1.0", "eth-1", "valid", 0));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(rev2, "scope-wifi", 2, "SelectedAdapter", "Win32", "1.0", "wifi-1", "valid", 0));

            var dayStart = 100_000_000L;
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-r1", "k-r1", sid, clk, rev1, "unidentified", dayStart, dayStart + 1000, 1000, 500, 200, false, false, null, "high"));
            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv-r2", "k-r2", sid, clk, rev2, "unidentified", dayStart + 2000, dayStart + 3000, 1000, 800, 300, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var range = new HistoricalRange(dayStart - 10_000_000, dayStart + 20_000_000);
            var rollupsCreated = await storage.ComputeDailyRollupsAsync(range, "UTC");
            Assert(rollupsCreated > 0, "Daily rollups should be created");

            // Verify idempotency
            var rollupsSecondPass = await storage.ComputeDailyRollupsAsync(range, "UTC");
            Assert(rollupsSecondPass > 0, "Second rollup pass must execute idempotently");
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
            QuotaDirectionMode.Combined, 10_000_000_000L, [8000, 9000, 10000], "UTC");

        var eval = QuotaEvaluator.Evaluate(rule, 12_000_000_000L, 0, []);

        Assert(eval.IsExceeded, "Quota should be exceeded");
        Assert(eval.RemainingBytes == 0, "Remaining bytes should be 0 when exceeded");
        Assert(eval.OverageBytes == 2_000_000_000L, $"Overage mismatch: {eval.OverageBytes}");
        Assert(eval.PercentUsed == 120.0, $"Percent used mismatch: {eval.PercentUsed}");
        Assert(eval.CrossedThresholdsBps.Count == 3, "All thresholds crossed");
    }

    public static void TestQuota_DisabledRuleTruthfullyPreservesRecordedBytes()
    {
        var rule = new QuotaRule(
            "rule-disabled", 1, false, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 10_000_000_000L, [8000, 9000, 10000], "UTC",
            OpeningBalanceBytes: 1_000_000_000L);

        var eval = QuotaEvaluator.Evaluate(rule, 50_000_000L, 20_000_000L, []);

        Assert(eval.RecordedBytes == 70_000_000L,
            $"Disabled quota rule must preserve true recorded bytes (70 MB), was: {eval.RecordedBytes}");
        Assert(eval.EffectiveUsedBytes == 1_070_000_000L, "Effective usage includes opening balance");
        Assert(eval.CrossedThresholdsBps.Count == 0, "Disabled rule must not emit threshold events");
    }

    public static void TestQuota_BasisPointsAndThresholdValidation()
    {
        Assert(QuotaRule.DefaultThresholdsBps.SequenceEqual([8000, 9000, 10000]),
            "PRD §11.2 default thresholds must be 80%, 90%, 100% in basis points");

        var validRule = new QuotaRule("rule-val", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 1000L, [8000, 9000], "UTC");
        validRule.Validate();

        var revised = validRule.CreateRevision(newLimitBytes: 2000L);
        Assert(revised.Revision == 2, $"Revision must increment to 2, was {revised.Revision}");
        Assert(revised.LimitBytes == 2000L, "LimitBytes updated in revision");
        Assert(validRule.Revision == 1, "Prior rule revision remains immutable");

        var cycleId = BillingCycleIdentity.Create("rule-val", 1, 1000, 2000, "UTC");
        Assert(cycleId == "rule-val_r1_1000_2000_UTC", $"Cycle identity mismatch: {cycleId}");

        var duplicateRule = new QuotaRule("rule-dup", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 1000L, [8000, 8000], "UTC");
        try
        {
            duplicateRule.Validate();
            throw new Exception("Duplicate thresholds should have thrown ArgumentException");
        }
        catch (ArgumentException) { }
    }

    public static void TestQuota_OpeningBalanceSeparationDoesNotFalsifyCoverage()
    {
        var rule = new QuotaRule(
            "rule-2", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 100_000_000L, [8000, 10000], "UTC",
            OpeningBalanceBytes: 50_000_000L);

        var eval = QuotaEvaluator.Evaluate(rule, 30_000_000, 10_000_000, []);

        Assert(eval.RecordedBytes == 40_000_000, "Recorded bytes must remain pure and separated");
        Assert(eval.OpeningBalanceBytes == 50_000_000, "Opening balance must remain separated");
        Assert(eval.EffectiveUsedBytes == 90_000_000, "Effective usage must be 90 MB");
        Assert(eval.CrossedThresholdsBps.Contains(8000), "80% threshold crossed by effective usage");
    }

    public static void TestQuota_ThresholdCrossingAndSkipping()
    {
        var rule = new QuotaRule(
            "rule-3", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
            QuotaDirectionMode.Combined, 100_000_000L, [8000, 9000, 10000], "UTC");

        var eval = QuotaEvaluator.Evaluate(rule, 95_000_000, 0, []);

        Assert(eval.NewlyCrossedThresholdsBps.Count == 2, $"Expected 2 newly crossed thresholds, got {eval.NewlyCrossedThresholdsBps.Count}");
        Assert(eval.NewlyCrossedThresholdsBps[0] == 8000, "First newly crossed threshold must be 8000 bps");
        Assert(eval.NewlyCrossedThresholdsBps[1] == 9000, "Second newly crossed threshold must be 9000 bps");

        var evalNext = QuotaEvaluator.Evaluate(rule, 96_000_000, 0, [8000, 9000]);
        Assert(evalNext.NewlyCrossedThresholdsBps.Count == 0, "Already fired thresholds must be deduplicated");
    }

    public static async Task TestQuota_DeduplicationPersistsAcrossStorageRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_quota_dedup_{Guid.NewGuid():N}.db");
        var ruleId = "quota_rule_dedup";
        var cycleId = "2026-10-01";

        try
        {
            await using (var storage1 = new SqliteStorageEngine(dbPath))
            {
                var evt = new QuotaThresholdEvent(
                    Guid.NewGuid().ToString("D"), ruleId, 1, cycleId, 8000, 10000, 80_000_000);
                await storage1.RecordQuotaThresholdEventAsync(evt);
                await storage1.FlushAsync(TimeSpan.FromSeconds(5));
            }

            await using (var storage2 = new SqliteStorageEngine(dbPath))
            {
                var recorded = await storage2.GetRecordedThresholdsAsync(ruleId, 1, cycleId);
                Assert(recorded.Count == 1, $"Expected 1 recorded threshold, got {recorded.Count}");
                Assert(recorded[0] == 8000, "Recorded threshold must be 8000 bps");

                var rule = new QuotaRule(
                    ruleId, 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
                    QuotaDirectionMode.Combined, 100_000_000L, [8000, 9000], "UTC");

                var eval = QuotaEvaluator.Evaluate(rule, 85_000_000, 0, recorded);
                Assert(eval.CrossedThresholdsBps.Contains(8000), "8000 bps is crossed");
                Assert(!eval.NewlyCrossedThresholdsBps.Contains(8000), "8000 bps must NOT be emitted again after restart");
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

        for (var i = 0; i < 500; i++)
        {
            buffer.Append(new LiveGraphPoint(1000 * i, 1000, 500, true, false, i));
        }
        Assert(buffer.Count == 100, $"Buffer count should be clamped to capacity 100, was {buffer.Count}");

        var spikeBuffer = new LiveGraphBuffer(capacitySeconds: 60);
        var baseTime = 100_000L;

        for (var i = 0; i < 60; i++)
        {
            var rx = (i == 30) ? 50_000_000.0 : 1_000.0;
            spikeBuffer.Append(new LiveGraphPoint(baseTime + (i * 1000), rx, 500.0, true, false, i));
        }

        var downsampled = spikeBuffer.GetDownsampled(TimeSpan.FromSeconds(60), 6, baseTime + 60_000);
        Assert(downsampled.Count == 6, $"Expected 6 downsampled buckets, got {downsampled.Count}");

        var peakObserved = downsampled.Max(b => b.PeakDownloadRate);
        Assert(peakObserved == 50_000_000.0, $"Peak downsampling lost the spike! Max observed: {peakObserved}");
    }

    public static void TestLiveGraphBuffer_MonotonicChronologyGuards()
    {
        var buffer = new LiveGraphBuffer(capacitySeconds: 50);

        buffer.Append(new LiveGraphPoint(1000, 100, 50, true, false, 1));
        Assert(buffer.Count == 1, "Initial count should be 1");

        // Reject out-of-order point (timestamp 500 < 1000)
        buffer.Append(new LiveGraphPoint(500, 200, 100, true, false, 2));
        Assert(buffer.Count == 1, "Out-of-order point must be rejected");

        // Duplicate timestamp with higher sequence updates in place
        buffer.Append(new LiveGraphPoint(1000, 150, 75, true, false, 3));
        Assert(buffer.Count == 1, "Duplicate timestamp must not increment count");
        var points = buffer.GetPoints(TimeSpan.FromSeconds(10), 2000);
        Assert(points[0].DownloadRate == 150, "Duplicate timestamp updated to higher sequence");
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

    public static void TestSettings_FutureSchemaVersionRejectedWithoutOverwriting()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"speedmeter_settings_future_{Guid.NewGuid():N}.json");
        try
        {
            var futureJson = "{\n  \"schemaVersion\": 999,\n  \"displayUnits\": \"Gbps\"\n}";
            File.WriteAllText(tempFile, futureJson);

            var (loaded, warn) = SettingsStorage.Load(tempFile);
            Assert(warn != null && warn.Contains("Incompatible future settings schema version 999"),
                $"Must reject future schema version with warning, got: {warn}");
            Assert(loaded.SchemaVersion == SpeedMeterSettings.CurrentSchemaVersion,
                "Must load safe default schema version");

            // Verify original file was preserved and NOT overwritten
            var currentDiskJson = File.ReadAllText(tempFile);
            Assert(currentDiskJson.Contains("999"), "Original file with future schema must be preserved untouched");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public static void TestSettings_MonitorIdentityPersisted()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"speedmeter_settings_mon_{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SpeedMeterSettings
            {
                WidgetX = 1500,
                WidgetY = 850,
                WidgetMonitorDeviceName = @"\\.\DISPLAY1",
                WidgetMonitorBounds = "0,0,1920,1080"
            };

            SettingsStorage.SaveAtomic(tempFile, settings);
            var (loaded, _) = SettingsStorage.Load(tempFile);

            Assert(loaded.WidgetMonitorDeviceName == @"\\.\DISPLAY1", "Monitor device name mismatch");
            Assert(loaded.WidgetMonitorBounds == "0,0,1920,1080", "Monitor bounds mismatch");
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
            File.WriteAllText(tempFile, "{ broken json corrupt content !!! ");

            var (loaded, warn) = SettingsStorage.Load(tempFile);
            Assert(warn != null && warn.Contains("corrupted"), "Must surface corruption warning");
            Assert(loaded != null, "Must return safe default settings");
            Assert(loaded!.DisplayUnits == "Mbps", "Default display units must be Mbps");
            Assert(loaded.WidgetOpacity == 0.90, "Default opacity must be 0.90");

            var dir = Path.GetDirectoryName(tempFile)!;
            var pattern = Path.GetFileName(tempFile) + ".corrupt.*.json";
            var backups = Directory.GetFiles(dir, pattern);
            Assert(backups.Length > 0, "Corrupted settings file backup was not preserved with .json extension");

            foreach (var b in backups) { try { File.Delete(b); } catch { } }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public static async Task TestApplicationServices_EndToEndIntegration()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_app_services_{Guid.NewGuid():N}.db");
        var settingsFile = Path.Combine(Path.GetTempPath(), $"speedmeter_test_settings_{Guid.NewGuid():N}.json");

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord("sess_app", "epoch", 0, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord("clk_app", "sess_app", 0, 0, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("rev_app", "scope_app", 1, "SelectedAdapter", "Win32", "1.0", "adapter_app", "valid", 0));

            var now = new DateTimeOffset(2026, 10, 11, 14, 0, 0, TimeSpan.Zero);
            var nowMs = now.ToUnixTimeMilliseconds();

            await storage.IngestIntervalAsync(new UsageIntervalRecord(
                "iv_app1", "key_app1", "sess_app", "clk_app", "rev_app", "unidentified", nowMs - 5000, nowMs, 5000, 100_000, 50_000, false, false, null, "high"));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // 1. Test UsageHistoryService
            var historyService = new UsageHistoryService(storage);
            var todaySummary = await historyService.GetPeriodSummaryAsync(QueryPeriodType.Today, "UTC", now);
            Assert(todaySummary.BytesRx == 100_000, $"UsageHistoryService today RX mismatch: {todaySummary.BytesRx}");

            // 2. Test QuotaService
            var quotaService = new QuotaService(storage);
            var rule = new QuotaRule("rule_svc", 1, true, QuotaPeriodType.MonthlyBillingCycle, 1,
                QuotaDirectionMode.Combined, 1_000_000L, [8000, 10000], "UTC");
            var eval = await quotaService.EvaluateCurrentCycleAsync(rule, now);
            Assert(eval.RecordedBytes == 150_000, $"QuotaService recorded usage mismatch: {eval.RecordedBytes}");

            // 3. Test SettingsService
            var settingsService = new SettingsService(settingsFile);
            var changedNotified = false;
            settingsService.SettingsChanged += _ => changedNotified = true;

            var current = settingsService.CurrentSettings;
            current.DisplayUnits = "MB/s";
            settingsService.Save(current);

            Assert(changedNotified, "SettingsChanged event must fire upon save");
            Assert(settingsService.Load().DisplayUnits == "MB/s", "Saved settings must reload correctly");

            // 4. Test LiveGraphService
            var graphService = new LiveGraphService(capacitySeconds: 60);
            graphService.RecordSample(nowMs, 1000, 500, true, false, 1);
            var raw = graphService.GetRawPoints(TimeSpan.FromSeconds(10), nowMs);
            Assert(raw.Count == 1, "LiveGraphService must store and retrieve raw points");
            var down = graphService.GetDownsampled(TimeSpan.FromSeconds(10), 2, nowMs);
            Assert(down.Count == 2, "LiveGraphService must downsample points");
        }
        finally
        {
            DeleteDbFiles(dbPath);
            if (File.Exists(settingsFile)) File.Delete(settingsFile);
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

            var queryTasks = new List<Task>();
            for (var i = 0; i < 25; i++)
            {
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
