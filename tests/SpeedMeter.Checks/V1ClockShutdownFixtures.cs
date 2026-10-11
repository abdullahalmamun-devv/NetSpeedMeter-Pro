using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

/// <summary>Independent lifecycle and clock-boundary checks for AC-M05 and AC-M08.</summary>
public static class V1ClockShutdownFixtures
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public static void Run(Action<string, Action> check)
    {
        check("V1-CLOCK: stable pause preserves observed UTC bounds and monotonic duration", StablePause);
        check("V1-CLOCK: backward UTC jump records a point gap, new segment, and uncertain history", BackwardPauseJump);
        check("V1-CLOCK: forward UTC jump does not invent an hour of missing coverage", ForwardPauseJump);
        check("V1-CLOCK: repeated UTC labels retain both byte intervals without double-counting coverage", BackwardUtcLabelCollision);
        check("V1-CLOCK: dispose after UTC jump persists monotonic gap and new segment", DisposeAfterJump);
        check("V1-CLOCK: stable suspend uses QPC elapsed duration and observed UTC bounds", StableSuspend);
        check("V1-CLOCK: suspend with UTC jump records an uncertain point gap", SuspendWithJump);
        check("V1-CLOCK: missed suspend notification is detected from awake-time discontinuity", MissedSuspendNotification);
        check("V1-CLOCK: pause requested during suspend starts at wake without overlap", PauseDuringSuspend);
        check("V1-SHUTDOWN: failed pause-gap write surfaces a diagnostic without a clean claim", () => ShutdownFailure(FailureMode.Gap, "gap"));
        check("V1-SHUTDOWN: failed session end surfaces a diagnostic without a clean claim", () => ShutdownFailure(FailureMode.SessionEnd, "session_end"));
        check("V1-SHUTDOWN: failed final flush surfaces a diagnostic without a clean claim", () => ShutdownFailure(FailureMode.FinalFlush, "final_flush"));
        check("V1-SHUTDOWN: nonreturning flush is bounded and reported", HangingFlush);
        check("V1-CLOCK-WINDOWS: production clock exposes awake-only interrupt time", NativeAwakeClock);
    }

    private sealed class TestClock : IMeasurementClock
    {
        public double MonotonicSeconds { get; private set; } = 100;
        public DateTimeOffset UtcNow { get; private set; } = Noon;
        public double? AwakeSeconds { get; private set; } = 100;
        public ClockReading Read() => new(MonotonicSeconds, UtcNow, AwakeSeconds);
        public void AdvanceBoth(double seconds)
        {
            MonotonicSeconds += seconds;
            UtcNow = UtcNow.AddSeconds(seconds);
            if (AwakeSeconds.HasValue) AwakeSeconds += seconds;
        }
        public void AdvanceMonotonicOnly(double seconds) => MonotonicSeconds += seconds;
        public void ShiftUtc(TimeSpan span) => UtcNow = UtcNow.Add(span);
        public void AdvanceDuringUnreportedSleep(double seconds)
        {
            MonotonicSeconds += seconds;
            UtcNow = UtcNow.AddSeconds(seconds);
            // AwakeSeconds intentionally does not advance.
        }
    }

    private sealed class TestProvider : IAdapterCounterProvider
    {
        public ulong Rx { get; set; } = 1000;
        public ulong Tx { get; set; } = 500;
        public AdapterObservation ReadSnapshot() => new(
            ProviderAvailability.Available,
            [new AdapterCounter("nic-clock", "Ethernet", "Fixture", true, Rx, Tx, 1_000_000_000)],
            "fixture", "1", "Available");
    }

    private sealed class TestRoute : IRouteProvider
    {
        public IReadOnlyList<AdapterScopeCandidate> GetCandidates() =>
        [
            new("nic-clock", "Ethernet", "Fixture", true, true, 1_000_000_000,
                InterfaceClassification.PhysicalEthernet, true, 1)
        ];
    }

    private enum FailureMode { Gap, SessionEnd, FinalFlush, HangingFlush }

    private sealed class FailingStorage : IStorageEngine
    {
        private readonly FailureMode failure;
        private int flushCount;
        public FailingStorage(FailureMode failure) => this.failure = failure;
        public StorageStatus Status => new(StorageHealth.Healthy, 0, 0, 0, null);
        public bool CleanEndCommitted { get; private set; }
        public Task RecordSessionStartAsync(SessionRecord session, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordSessionEndAsync(string sessionId, DateTimeOffset endedAtUtc, bool endedCleanly, string? stopReason, CancellationToken ct = default)
        {
            if (failure == FailureMode.SessionEnd) return Task.FromException(new IOException("session end failed"));
            CleanEndCommitted = endedCleanly;
            return Task.CompletedTask;
        }
        public Task RecordClockSegmentAsync(ClockSegmentRecord segment, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeRevisionAsync(ScopeRevisionRecord revision, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeMembershipAsync(ScopeMembershipRecord membership, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordCheckpointAsync(CounterCheckpointRecord checkpoint, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestIntervalAsync(UsageIntervalRecord interval, CounterCheckpointRecord? checkpoint = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestGapAsync(UsageIntervalRecord gap, CancellationToken ct = default) =>
            failure == FailureMode.Gap ? Task.FromException(new IOException("gap write failed")) : Task.CompletedTask;
        public Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default) => Task.FromResult<SessionRecord?>(null);
        public Task<long?> GetLastDurableTimestampUtcMsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);
        public Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult((0L, 0L));
        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetHistoryAvailabilityAsync(CancellationToken ct = default) => Task.FromResult<(long?, long?)>((null, null));
        public Task<HistoricalSummary> GetUsageSummaryAsync(HistoricalRange range, string reportingTimezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default) =>
            Task.FromResult(new HistoricalSummary(range, reportingTimezoneId, 0, 0, 0, 0, 0, CompletenessState.NoCoverage));
        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>([]);
        public Task<int> ComputeDailyRollupsAsync(HistoricalRange range, string reportingTimezoneId, CancellationToken ct = default) => Task.FromResult(0);
        public Task RecordQuotaThresholdEventAsync(QuotaThresholdEvent record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<int>>([]);
        public Task FlushAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref flushCount);
            if (failure == FailureMode.HangingFlush) return Task.Delay(Timeout.InfiniteTimeSpan);
            return failure == FailureMode.FinalFlush && call == 2
                ? Task.FromException(new IOException("final flush failed"))
                : Task.CompletedTask;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"speedmeter_clock_{Guid.NewGuid():N}.db");
        public TestClock Clock { get; } = new();
        public TestProvider Provider { get; } = new();
        public SqliteStorageEngine Storage { get; }
        public Collector Collector { get; }

        public Fixture()
        {
            Storage = new SqliteStorageEngine(Path);
            Collector = new Collector(Provider, Clock, new TestRoute(), Storage, routeDebounceSeconds: 0);
        }

        public void SeedValidInterval()
        {
            Collector.SampleOnce();
            Clock.AdvanceBoth(1);
            Provider.Rx += 100;
            Provider.Tx += 20;
            var result = Collector.SampleOnce();
            Require(result.Reading.IntervalBytesRx == 100 && result.Reading.IntervalBytesTx == 20,
                "Pre-gap valid interval did not record the expected integer deltas.");
        }

        public void SeedPostGapValidInterval()
        {
            Clock.AdvanceBoth(1);
            Provider.Rx += 900;
            Collector.SampleOnce(); // Fresh baseline; the 900 bytes while paused remain unmeasured.
            Clock.AdvanceBoth(1);
            Provider.Rx += 37;
            Provider.Tx += 10;
            var result = Collector.SampleOnce();
            Require(result.Reading.IntervalBytesRx == 37 && result.Reading.IntervalBytesTx == 10,
                "Post-gap measurement must exclude bytes from the unobserved period.");
        }

        public IReadOnlyList<UsageIntervalRecord> Intervals()
        {
            Storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return Storage.GetIntervalsAsync().GetAwaiter().GetResult();
        }

        public IReadOnlyList<(string Id, long MonoTicks, long UtcMs, string Confidence, string? Reason)> Segments()
        {
            Storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            using var connection = new SqliteConnection($"Data Source={Path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT segment_id, monotonic_anchor_ticks, utc_anchor_ms, timing_confidence, change_reason FROM clock_segments ORDER BY monotonic_anchor_ticks";
            using var reader = command.ExecuteReader();
            var segments = new List<(string, long, long, string, string?)>();
            while (reader.Read())
            {
                segments.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
            return segments;
        }

        public void Dispose()
        {
            Collector.Dispose();
            Storage.Dispose();
            SqliteConnection.ClearAllPools();
            try { File.Delete(Path); } catch { }
            try { File.Delete(Path + "-wal"); } catch { }
            try { File.Delete(Path + "-shm"); } catch { }
        }
    }

    private static void StablePause()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Clock.AdvanceBoth(1);
        var pauseUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Pause();
        fixture.Clock.AdvanceBoth(30);
        var resumeUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Resume();

        var gap = SingleGap(fixture.Intervals());
        Require(gap.GapReason == "user_paused", "Wrong pause gap reason.");
        Require(gap.StartUtcMs == pauseUtcMs && gap.EndUtcMs == resumeUtcMs,
            "Stable wall clock must use the two observed UTC boundaries.");
        Require(gap.MonotonicDurationTicks == 300_000_000, "Pause elapsed duration must come from monotonic time.");
        Require(gap.TimingConfidence == "nominal", "Stable pause should retain nominal clock confidence.");
        Require(fixture.Segments().Count == 1, "Stable pause must not create a clock discontinuity segment.");
    }

    private static void BackwardPauseJump() => AssertPauseJump(TimeSpan.FromMinutes(-20));
    private static void ForwardPauseJump() => AssertPauseJump(TimeSpan.FromHours(1));

    private static void AssertPauseJump(TimeSpan jump)
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Clock.AdvanceBoth(1);
        var pauseUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Pause();
        fixture.Clock.AdvanceBoth(30);
        fixture.Clock.ShiftUtc(jump);
        var resumeUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        var resumeMonotonicTicks = (long)(fixture.Clock.MonotonicSeconds * 10_000_000);
        fixture.Collector.Resume();
        fixture.SeedPostGapValidInterval();

        var intervals = fixture.Intervals();
        var gap = SingleGap(intervals);
        Require(gap.GapReason == "user_paused", "Wrong pause gap reason after wall-clock change.");
        Require(gap.StartUtcMs == pauseUtcMs && gap.EndUtcMs == pauseUtcMs,
            "Clock-jump marker must stay at the observed pause UTC anchor without a projected endpoint.");
        Require(gap.MonotonicDurationTicks == 300_000_000,
            "Clock-jump marker lost the actual thirty-second monotonic duration.");
        Require(gap.TimingConfidence == "utc_discontinuous", "Clock-jump marker must disclose UTC uncertainty.");
        Require(gap.StartUtcMs != resumeUtcMs, "Fixture did not actually move UTC independently of monotonic time.");

        var segments = fixture.Segments();
        Require(segments.Count == 2, $"Expected initial and revised clock segments, got {segments.Count}.");
        Require(segments[0].Id == gap.ClockSegmentId, "Gap belongs to the segment active at pause start.");
        Require(segments[1].Id != segments[0].Id, "Wall-clock discontinuity must create a distinct segment ID.");
        Require(segments[1].MonoTicks == resumeMonotonicTicks && segments[1].UtcMs == resumeUtcMs,
            "New clock segment must anchor the actually observed resume clocks.");
        Require(segments[1].Reason == "wall_clock_change", "New clock segment must identify wall-clock change.");

        var valid = intervals.Where(x => !x.IsGap).ToArray();
        Require(valid.Length == 2, "Expected one valid interval on each side of the clock jump.");
        Require(valid.Sum(x => x.BytesRx) == 137 && valid.Sum(x => x.BytesTx) == 30,
            "Clock change must conserve measured integer deltas without counting paused traffic.");
        Require(valid.Single(x => x.BytesRx == 100).ClockSegmentId == segments[0].Id,
            "Earlier valid interval must remain in the original segment.");
        Require(valid.Single(x => x.BytesRx == 37).ClockSegmentId == segments[1].Id,
            "Future valid interval must use the revised clock segment.");
        AssertNoUtcCoverageOverlap(intervals);

        var lower = Math.Min(valid.Min(x => x.StartUtcMs), pauseUtcMs) - 1_000;
        var upper = Math.Max(valid.Max(x => x.EndUtcMs), resumeUtcMs) + 1_000;
        var summary = fixture.Storage.GetUsageSummaryAsync(new HistoricalRange(lower, upper), "UTC")
            .GetAwaiter().GetResult();
        Require(summary.BytesRx == 137 && summary.BytesTx == 30,
            "Historical query must preserve exact bytes across a clock segment revision.");
        Require(summary.MissingDurationMs == 0,
            "Point marker must not be stretched into fabricated calendar missing coverage.");
        Require(summary.IsEstimated && summary.Completeness == CompletenessState.ClockUncertain,
            "Historical query must expose clock-mapping uncertainty explicitly.");
    }

    private static void BackwardUtcLabelCollision()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Clock.AdvanceBoth(1);
        fixture.Collector.Pause();
        fixture.Clock.AdvanceBoth(30);
        fixture.Clock.ShiftUtc(TimeSpan.FromSeconds(-32.5));
        fixture.Collector.Resume();
        fixture.SeedPostGapValidInterval();

        var valid = fixture.Intervals().Where(x => !x.IsGap).OrderBy(x => x.StartUtcMs).ToArray();
        Require(valid.Length == 2, "Both valid measurements must survive repeated UTC labels.");
        Require(valid[0].ClockSegmentId != valid[1].ClockSegmentId,
            "Repeated UTC labels must retain distinct clock-segment identities.");
        Require(valid[0].StartUtcMs == Noon.ToUnixTimeMilliseconds() &&
                valid[0].EndUtcMs == Noon.AddSeconds(1).ToUnixTimeMilliseconds() &&
                valid[1].StartUtcMs == Noon.AddMilliseconds(500).ToUnixTimeMilliseconds() &&
                valid[1].EndUtcMs == Noon.AddMilliseconds(1500).ToUnixTimeMilliseconds(),
            "Valid intervals must retain the observed UTC labels, even when a backward jump repeats them.");
        Require(valid.Sum(x => x.BytesRx) == 137 && valid.Sum(x => x.BytesTx) == 30,
            "Repeated UTC labels must not discard or duplicate measured bytes.");

        // Literal UTC labels overlap by 500 ms across segments. No single global
        // wall-clock ordering can remove that collision without inventing time.
        Require(valid[0].EndUtcMs - valid[1].StartUtcMs == 500,
            "Fixture must exercise a real 500 ms cross-segment UTC-label collision.");
        var range = new HistoricalRange(Noon.AddSeconds(-1).ToUnixTimeMilliseconds(),
            Noon.AddSeconds(3).ToUnixTimeMilliseconds());
        var summary = fixture.Storage.GetUsageSummaryAsync(range, "UTC").GetAwaiter().GetResult();
        Require(summary.BytesRx == 137 && summary.BytesTx == 30,
            "Historical query must keep exact bytes from both clock segments.");
        Require(summary.ObservedDurationMs == 1_500,
            "Historical observed coverage must use the UTC union, not sum the repeated 500 ms twice.");
        Require(summary.MissingDurationMs == 0 && summary.CoverageRatio <= 1,
            "Point marker must not invent missing coverage or let coverage exceed 100 percent.");
        Require(summary.IsEstimated && summary.Completeness == CompletenessState.ClockUncertain,
            "Repeated UTC labels require explicit uncertain clock-mapping status.");
    }

    private static void DisposeAfterJump()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Clock.AdvanceBoth(1);
        var pauseUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Pause();
        fixture.Clock.AdvanceBoth(25);
        fixture.Clock.ShiftUtc(TimeSpan.FromMinutes(-20));
        var shutdownUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Dispose();

        var gap = SingleGap(fixture.Intervals());
        Require(gap.StartUtcMs == pauseUtcMs && gap.EndUtcMs == pauseUtcMs,
            "Shutdown after a clock jump must persist a point marker, not a false UTC span.");
        Require(gap.MonotonicDurationTicks == 250_000_000 && gap.TimingConfidence == "utc_discontinuous",
            "Shutdown gap must preserve twenty-five seconds of monotonic duration and uncertainty.");
        var segments = fixture.Segments();
        Require(segments.Count == 2 && segments[1].UtcMs == shutdownUtcMs,
            "Shutdown after clock jump must persist the new observed UTC anchor.");
        var session = fixture.Storage.GetLastSessionAsync().GetAwaiter().GetResult();
        Require(session is { EndedCleanly: true }, "Successful shutdown must end the session cleanly.");
    }

    private static void StableSuspend()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Collector.SetSuspended(true);
        var suspendUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Clock.AdvanceBoth(90);
        var resumeUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.SetSuspended(false);

        var gap = SingleGap(fixture.Intervals());
        Require(gap.GapReason == "system_sleep", "Wrong system-sleep reason.");
        Require(gap.StartUtcMs == suspendUtcMs && gap.EndUtcMs == resumeUtcMs,
            "Stable suspend must use observed UTC anchors.");
        Require(gap.MonotonicDurationTicks == 900_000_000,
            "Suspend duration must use the monotonic source that advances through Windows sleep.");
    }

    private static void SuspendWithJump()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        fixture.Collector.SetSuspended(true);
        var suspendUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Clock.AdvanceBoth(90);
        fixture.Clock.ShiftUtc(TimeSpan.FromHours(1));
        var resumeUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.SetSuspended(false);

        var gap = SingleGap(fixture.Intervals());
        Require(gap.GapReason == "system_sleep", "Wrong system-sleep reason after UTC jump.");
        Require(gap.StartUtcMs == suspendUtcMs && gap.EndUtcMs == suspendUtcMs,
            "Sleep clock change must not turn one and a half minutes into an hour-long UTC gap.");
        Require(gap.MonotonicDurationTicks == 900_000_000 && gap.TimingConfidence == "utc_discontinuous",
            "Sleep clock change must preserve QPC elapsed duration with explicit uncertainty.");
        var segments = fixture.Segments();
        Require(segments.Count == 2 && segments[1].UtcMs == resumeUtcMs,
            "Sleep with clock change must start a new observed clock segment.");
    }

    private static void MissedSuspendNotification()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        var lastObservedUtcMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Clock.AdvanceDuringUnreportedSleep(90);
        fixture.Provider.Rx += 900;
        var firstAfterWake = fixture.Collector.SampleOnce();
        Require(firstAfterWake.Reading.IntervalBytesRx is null,
            "First sample after missed sleep must rebaseline, not count ninety seconds of traffic.");
        fixture.Clock.AdvanceBoth(1);
        fixture.Provider.Rx += 37;
        var next = fixture.Collector.SampleOnce();
        Require(next.Reading.IntervalBytesRx == 37,
            "First valid post-sleep interval must count only newly observed bytes.");

        var intervals = fixture.Intervals();
        var gap = SingleGap(intervals);
        Require(gap.GapReason == "system_sleep", "Missed suspend must record a system-sleep gap.");
        Require(gap.MonotonicDurationTicks == 900_000_000,
            "Missed suspend must retain actual total monotonic elapsed duration.");
        Require(gap.StartUtcMs == lastObservedUtcMs,
            "Missed suspend gap must begin at the last valid observation.");
        Require(intervals.Where(x => !x.IsGap).Sum(x => x.BytesRx) == 137,
            "Missed suspend must exclude unobserved bytes from persisted totals.");
        AssertNoUtcCoverageOverlap(intervals);
    }

    private static void PauseDuringSuspend()
    {
        using var fixture = new Fixture();
        fixture.SeedValidInterval();
        var suspendStartMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.SetSuspended(true);
        fixture.Clock.AdvanceBoth(10);
        fixture.Collector.Pause();
        fixture.Clock.AdvanceBoth(30);
        var wakeMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.SetSuspended(false);
        fixture.Clock.AdvanceBoth(10);
        var userResumeMs = fixture.Clock.UtcNow.ToUnixTimeMilliseconds();
        fixture.Collector.Resume();

        var gaps = fixture.Intervals().Where(x => x.IsGap).OrderBy(x => x.StartUtcMs).ToArray();
        Require(gaps.Length == 2, "Sleep followed by user pause must record exactly two gaps.");
        Require(gaps[0].GapReason == "system_sleep" && gaps[0].StartUtcMs == suspendStartMs &&
                gaps[0].EndUtcMs == wakeMs && gaps[0].MonotonicDurationTicks == 400_000_000,
            "Sleep must own the full forty-second span until wake.");
        Require(gaps[1].GapReason == "user_paused" && gaps[1].StartUtcMs == wakeMs &&
                gaps[1].EndUtcMs == userResumeMs && gaps[1].MonotonicDurationTicks == 100_000_000,
            "Pause requested during suspend must begin at wake and end on user resume.");
        AssertNoUtcCoverageOverlap(gaps);
    }

    private static void ShutdownFailure(FailureMode failure, string expectedStage)
    {
        var storage = new FailingStorage(failure);
        var clock = new TestClock();
        using var collector = new Collector(new TestProvider(), clock, storage: storage);
        collector.Pause();
        clock.AdvanceBoth(5);

        var stopwatch = Stopwatch.StartNew();
        collector.Dispose();
        stopwatch.Stop();
        Require(stopwatch.Elapsed < TimeSpan.FromSeconds(4), "Failed persistence must not stall shutdown.");
        var result = collector.ShutdownResult ?? throw new Exception("Collector did not publish a shutdown result.");
        Require(!result.CleanDurable, "Failed persistence must not claim clean durable shutdown.");
        Require(result.FailureStage == expectedStage,
            $"Expected shutdown failure stage '{expectedStage}', got '{result.FailureStage}'.");
        Require(!string.IsNullOrWhiteSpace(result.Diagnostic), "Shutdown failure must expose a diagnostic.");
        if (failure != FailureMode.FinalFlush)
            Require(!storage.CleanEndCommitted, "Failed gap or session-end persistence must not commit a clean session flag.");
    }

    private static void HangingFlush()
    {
        var storage = new FailingStorage(FailureMode.HangingFlush);
        var clock = new TestClock();
        var collector = new Collector(new TestProvider(), clock, storage: storage);
        collector.Pause();
        clock.AdvanceBoth(5);

        var disposeTask = Task.Run(collector.Dispose);
        Require(disposeTask.Wait(TimeSpan.FromSeconds(4)), "A nonreturning storage flush must not hang process shutdown.");
        var result = collector.ShutdownResult ?? throw new Exception("Collector did not publish a shutdown result.");
        Require(!result.CleanDurable && result.FailureStage == "gap_flush",
            "A timed-out gap flush must be reported as failed and not durable.");
        Require(!string.IsNullOrWhiteSpace(result.Diagnostic), "Timed-out flush must expose a diagnostic.");
    }

    private static void NativeAwakeClock()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipCheckException("Windows awake-time API is unavailable on this platform.");
        var clock = new MeasurementClock();
        var first = clock.Read();
        var second = clock.Read();
        Require(first.AwakeSeconds is { } firstAwake && second.AwakeSeconds is { } secondAwake &&
                double.IsFinite(firstAwake) && double.IsFinite(secondAwake) &&
                firstAwake >= 0 && secondAwake >= firstAwake,
            "Production MeasurementClock must expose monotonic awake-only time on this Windows host.");
    }

    private static UsageIntervalRecord SingleGap(IReadOnlyList<UsageIntervalRecord> intervals)
    {
        var gaps = intervals.Where(x => x.IsGap).ToArray();
        Require(gaps.Length == 1, $"Expected exactly one gap; found {gaps.Length}.");
        return gaps[0];
    }

    private static void AssertNoUtcCoverageOverlap(IReadOnlyList<UsageIntervalRecord> intervals)
    {
        var spans = intervals.Where(x => x.EndUtcMs > x.StartUtcMs)
            .OrderBy(x => x.StartUtcMs).ToArray();
        for (var i = 1; i < spans.Length; i++)
        {
            Require(spans[i - 1].EndUtcMs <= spans[i].StartUtcMs,
                "Positive-width persisted intervals overlap in calendar coverage.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
