using SpeedMeter.Core;

/// <summary>Deterministic domain fixtures; no timers, Windows APIs, or network requests.</summary>
public static class MeasurementFixtures
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    private static AdapterCounter Counter(ulong rx, ulong tx = 0, string id = "a",
        CounterEpoch epoch = default) => new(id, "Adapter", "Fixture", true, rx, tx, 0, SourceEpoch: epoch);
    private static MeasurementEngine Engine()
    {
        var engine = new MeasurementEngine();
        engine.SelectScope("a");
        return engine;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static Reading Sample(MeasurementEngine engine, AdapterCounter counter, double time, double expected = 1)
        => engine.Sample(counter, time, Utc, expected);

    public static void Run(Action<string, Action> check)
    {
        check("Epoch fixture: exact 125 MB / 10 seconds and simultaneous directions", () =>
        {
            var e = Engine();
            Sample(e, Counter(100, 50), 0);
            var r = Sample(e, Counter(125000100, 250000050), 10);
            Require(r.Received == 125000000 && r.Sent == 250000000, "Integer totals differ.");
            Require(r.Download == 12500000 && r.Upload == 25000000 && r.CoveredSeconds == 10, "Interval rates differ.");
            Require(MetricFormat.Rate(r.Download) == "12.50 MB/s" && MetricFormat.Rate(r.Download, true) == "100.00 Mbps", "Unit conversion differs.");
        });
        check("Epoch fixture: idle is live zero with observed coverage", () =>
        {
            var e = Engine();
            Sample(e, Counter(100, 200), 0);
            var r = Sample(e, Counter(100, 200), 1);
            Require(r.IsValid && r.Download == 0 && r.Upload == 0 && r.CoveredSeconds == 1, "Idle confused with missing.");
        });
        check("Epoch fixture: source epoch changes even with increasing counters", () =>
        {
            var e = Engine();
            var epoch1 = new CounterEpoch(Guid.NewGuid());
            var epoch2 = new CounterEpoch(Guid.NewGuid());
            Sample(e, Counter(0, epoch: epoch1), 0);
            var first = Sample(e, Counter(100, epoch: epoch1), 1);
            var changed = Sample(e, Counter(10000, epoch: epoch2), 2);
            var resumed = Sample(e, Counter(10020, epoch: epoch2), 3);
            Require(changed.State == ReadingState.Discontinuity && changed.Download is null && changed.Received == 100, "Cross-epoch bytes counted.");
            Require(changed.MeasurementEpoch > first.MeasurementEpoch && changed.SourceEpoch == epoch2, "New epoch metadata missing.");
            Require(resumed.IsValid && resumed.Received == 120 && resumed.CoveredSeconds == 2 && resumed.MeasurementEpoch == changed.MeasurementEpoch, "Epoch continuation incorrect.");
        });
        check("Epoch fixture: unknown-to-known source identity rebaselines", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            var r = Sample(e, Counter(100, epoch: new CounterEpoch(Guid.NewGuid())), 1);
            Require(r.State == ReadingState.Discontinuity && r.Received == 0, "Provider lifetime acquisition counted as continuity.");
        });
        check("Epoch fixture: either counter reset rejects both deltas", () =>
        {
            var e = Engine();
            var initial = Sample(e, Counter(100, 100), 0);
            var reset = Sample(e, Counter(200, 5), 1);
            var next = Sample(e, Counter(210, 15), 2);
            Require(reset.State == ReadingState.Discontinuity && reset.Received == 0 && reset.Sent == 0, "Reset partially accounted.");
            Require(reset.MeasurementEpoch > initial.MeasurementEpoch && next.Received == 10 && next.Sent == 10, "Reset baseline incorrect.");
        });
        check("Epoch fixture: irregular intervals use actual monotonic durations", () =>
        {
            var e = Engine();
            Sample(e, Counter(10, 10), 0);
            var a = Sample(e, Counter(60, 35), .25);
            var b = Sample(e, Counter(410, 210), 2);
            Require(a.Download == 200 && a.Upload == 100 && b.Download == 200 && b.Upload == 100, "Configured timer used as duration.");
            Require(b.Received == 400 && b.Sent == 200 && b.CoveredSeconds == 2, "Irregular accounting differs.");
        });
        check("Epoch fixture: coarse boundary and uninterrupted delayed delta", () =>
        {
            var e = Engine();
            var initial = Sample(e, Counter(0), 0);
            var normal = Sample(e, Counter(300), 3);
            var delayed = Sample(e, Counter(700), 7);
            Require(!normal.Coarse && delayed.Coarse && delayed.Download == 100 && delayed.Received == 700, "Coarse threshold or bytes incorrect.");
            Require(delayed.MeasurementEpoch == initial.MeasurementEpoch, "Delay fabricated an epoch.");
        });
        check("Epoch fixture: zero and negative elapsed rebaseline without accounting", () =>
        {
            foreach (var time in new[] { 10d, 9d })
            {
                var e = Engine();
                var first = Sample(e, Counter(0), 10);
                var invalid = Sample(e, Counter(100), time);
                var next = Sample(e, Counter(125), time + 1);
                Require(invalid.State == ReadingState.Discontinuity && invalid.Received == 0 && invalid.CoveredSeconds == 0, "Invalid interval committed.");
                Require(invalid.MeasurementEpoch > first.MeasurementEpoch && next.Received == 25, "Invalid clock baseline not replaced.");
            }
        });
        check("Epoch fixture: sleep gap preserves session and omits unobserved bytes", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            var prior = Sample(e, Counter(100), 1);
            var gap = e.Interrupt(Utc, ReadingState.Unavailable, "Suspended");
            var resume = Sample(e, Counter(10000), 100);
            var next = Sample(e, Counter(10050), 101);
            Require(gap.Download is null && resume.State == ReadingState.Measuring, "Gap is displayed as a rate.");
            Require(next.Received == 150 && next.CoveredSeconds == 2 && next.SessionId == prior.SessionId, "Sleep fabricated usage or discarded session.");
            Require(resume.MeasurementEpoch > prior.MeasurementEpoch, "Sleep did not create baseline generation.");
        });
        check("Epoch fixture: stable identity rename retains continuity", () =>
        {
            var e = Engine();
            var first = Sample(e, Counter(100), 0);
            var renamed = Sample(e, Counter(150) with { Name = "Renamed adapter" }, 1);
            Require(renamed.IsValid && renamed.Received == 50 && renamed.MeasurementEpoch == first.MeasurementEpoch, "Mutable name used as identity.");
        });
        check("Epoch fixture: selected scope rejects alternate identity and resets on scope selection", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            var prior = Sample(e, Counter(100), 1);
            var wrong = Sample(e, Counter(1000, id: "b"), 2);
            Require(wrong.State == ReadingState.Unavailable && wrong.ScopeId == "a" && wrong.Received == 100, "Wrong identity counted.");
            e.SelectScope("b");
            var selected = Sample(e, Counter(2000, id: "b"), 3);
            Require(selected.State == ReadingState.Measuring && selected.Received == 0 && selected.SessionId != prior.SessionId, "Scope sessions mixed.");
        });
        check("Epoch fixture: snapshot identity unique, session stable, published values unchanged", () =>
        {
            var e = Engine();
            var first = Sample(e, Counter(0), 0);
            var second = Sample(e, Counter(100), 1);
            e.SelectScope("a");
            var third = Sample(e, Counter(200), 2);
            Require(first.SnapshotId != Guid.Empty && second.SnapshotId != third.SnapshotId && first.SnapshotId != second.SnapshotId, "Snapshot identity reused.");
            Require(first.SessionId != Guid.Empty && first.SessionId == third.SessionId && first.MeasurementEpoch == third.MeasurementEpoch, "Ordinary sample changed session/epoch.");
            Require(first.Received == 0 && second.Received == 100 && third.Received == 200, "Published snapshot mutated.");
            e.ResetSession();
            var reset = Sample(e, Counter(1000), 3);
            Require(reset.SessionId != third.SessionId && reset.MeasurementEpoch > third.MeasurementEpoch && reset.Received == 0, "Reset metadata not renewed.");
        });
        check("Epoch fixture: wall-clock jumps do not alter monotonic accounting", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            var backward = e.Sample(Counter(100), 2, Utc.AddDays(-1), 1);
            var forward = e.Sample(Counter(200), 4, Utc.AddDays(1), 1);
            Require(backward.Download == 50 && forward.Download == 50 && forward.Received == 200 && forward.CoveredSeconds == 4, "Wall clock affected arithmetic.");
            Require(backward.MonotonicSeconds == 2 && forward.MonotonicSeconds == 4, "Canonical sample time missing.");
            Require(e.Interrupt(Utc, ReadingState.Paused, "Pause").MonotonicSeconds is null, "Interruption invented sample time.");
            Require(e.Interrupt(Utc, ReadingState.Paused, "Pause", 5).MonotonicSeconds == 5, "Explicit interruption time lost.");
            Require(e.Interrupt(Utc, ReadingState.Paused, "Pause", double.NaN).MonotonicSeconds is null, "Invalid interruption timestamp published.");
        });
        check("Epoch fixture: large 64-bit counters retain exact integer deltas", () =>
        {
            var e = Engine();
            Sample(e, Counter(ulong.MaxValue - 1000, ulong.MaxValue - 2000), 0);
            var r = Sample(e, Counter(ulong.MaxValue, ulong.MaxValue), 2);
            Require(r.Received == 1000 && r.Sent == 2000 && r.Download == 500 && r.Upload == 1000, "Counters were converted before subtraction.");
        });
        check("Epoch fixture: overflowing session direction leaves all accounting unchanged", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            Sample(e, Counter(100, ulong.MaxValue), 1);
            e.Interrupt(Utc, ReadingState.Paused, "Pause");
            Sample(e, Counter(0), 2);
            var invalid = Sample(e, Counter(10, 1), 3);
            Require(invalid.State == ReadingState.Discontinuity && invalid.Received == 100 && invalid.Sent == ulong.MaxValue && invalid.CoveredSeconds == 1, "Overflow partially committed accounting.");
        });
        check("Epoch fixture: nonfinite monotonic inputs interrupt continuity", () =>
        {
            foreach (var time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var e = Engine();
                Sample(e, Counter(0), 0);
                var invalid = Sample(e, Counter(100), time);
                var resume = Sample(e, Counter(1000), 2);
                Require(invalid.State == ReadingState.Discontinuity && invalid.Received == 0 && invalid.MonotonicSeconds is null && resume.State == ReadingState.Measuring, "Nonfinite clock accepted.");
            }
        });
        check("Epoch fixture: invalid expected intervals interrupt continuity", () =>
        {
            foreach (var expected in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var e = Engine();
                Sample(e, Counter(0), 0);
                var invalid = Sample(e, Counter(100), 1, expected);
                Require(invalid.State == ReadingState.Discontinuity && invalid.Download is null && invalid.Received == 0, "Invalid expected interval accepted.");
            }
        });
        check("Epoch fixture: finite timestamps whose subtraction overflows are rejected", () =>
        {
            var e = Engine();
            var first = Sample(e, Counter(0), -double.MaxValue);
            var invalid = Sample(e, Counter(100), double.MaxValue);
            Require(invalid.State == ReadingState.Discontinuity && invalid.Download is null && invalid.Received == 0 && invalid.CoveredSeconds == 0, "Infinite elapsed was accepted.");
            Require(invalid.MeasurementEpoch > first.MeasurementEpoch, "Infinite elapsed did not rebaseline.");
        });
        check("Epoch fixture: rate overflow is rejected before accounting", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), 0);
            var invalid = Sample(e, Counter(1, 1), double.Epsilon);
            var next = Sample(e, Counter(11, 21), 1);
            Require(invalid.State == ReadingState.Discontinuity && invalid.Received == 0 && invalid.Sent == 0 && invalid.CoveredSeconds == 0, "Infinite rate committed.");
            Require(next.IsValid && next.Received == 10 && next.Sent == 20, "Rate overflow baseline incorrect.");
        });
        check("Epoch fixture: coverage overflow cannot partially commit bytes", () =>
        {
            var e = Engine();
            Sample(e, Counter(0), -double.MaxValue);
            var first = Sample(e, Counter(1, 1), 0);
            var invalid = Sample(e, Counter(2, 2), double.MaxValue);
            Require(first.IsValid && first.CoveredSeconds == double.MaxValue, "Fixture baseline invalid.");
            Require(invalid.State == ReadingState.Discontinuity && invalid.Received == 1 && invalid.Sent == 1 && invalid.CoveredSeconds == double.MaxValue, "Coverage overflow committed bytes.");
        });
        check("Epoch fixture: formatter does not expose invalid numeric values", () =>
        {
            foreach (var value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Require(MetricFormat.Rate(value) == "—", "Invalid rate rendered as a measurement.");
            Require(MetricFormat.Rate(double.MaxValue, true) == "—", "Bit conversion overflow rendered as measurement.");
        });
    }
}
