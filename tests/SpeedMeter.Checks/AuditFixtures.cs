using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

/// <summary>Independent audit regressions. Callback scenarios execute in bounded child processes.</summary>
public static class AuditFixtures
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    private sealed class StepClock : IMeasurementClock
    {
        private long tick;
        public ClockReading Read() => new(Interlocked.Increment(ref tick) * .5, Utc);
    }

    private sealed class ManualClock : IMeasurementClock
    {
        public double Seconds { get; set; }
        public ClockReading Read() => new(Seconds, Utc);
    }

    private sealed class ProbeProvider : IAdapterCounterProvider
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);
        public ProviderAvailability Availability { get; set; } = ProviderAvailability.Available;
        public bool Empty { get; set; }
        public AdapterObservation ReadSnapshot()
        {
            var count = Interlocked.Increment(ref reads);
            AdapterCounter[] adapters = Empty ? [] : [new("a", "Audit", "Deterministic fixture", true,
                (ulong)count * 100, (ulong)count * 50, 0)];
            return new(Availability, adapters, "AuditFixture", "audit.status", "Audit fixture observation.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Until(Func<bool> predicate, string message, int milliseconds = 4000)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.ElapsedMilliseconds < milliseconds) Thread.Sleep(10);
        Require(predicate(), message);
    }

    public static int? TryRunChild(string[] args)
    {
        var argument = args.SingleOrDefault(a => a.StartsWith("--audit-child=", StringComparison.Ordinal));
        if (argument is null) return null;
        try
        {
            switch (argument["--audit-child=".Length..])
            {
                case "throw": ThrowingObserver(); break;
                case "slow": SlowObserver(); break;
                case "coalesce": CoalescedObserver(); break;
                case "dispose": CallbackDisposal(); break;
                default: throw new ArgumentException("Unknown audit child scenario.");
            }
            Console.WriteLine("Audit child passed: " + argument);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Audit child failed: " + ex);
            return 1;
        }
    }

    private static void RunChild(string scenario)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--audit-child=" + scenario);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Audit child did not launch.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(12000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new InvalidOperationException("Audit child exceeded 12-second deadline: " + scenario);
        }
        Task.WaitAll(stdout, stderr);
        Require(child.ExitCode == 0, $"Child {scenario} exit {child.ExitCode}: {stdout.Result}{stderr.Result}");
    }

    private static void ThrowingObserver()
    {
        var provider = new ProbeProvider();
        using var collector = new Collector(provider, new StepClock()) { Interval = .5 };
        collector.Select("a");
        var delivered = 0;
        collector.Updated += snapshot =>
        {
            if (snapshot.State == CollectorState.Running) throw new InvalidOperationException("Injected observer failure.");
        };
        collector.Updated += snapshot =>
        {
            if (snapshot.State == CollectorState.Running) Interlocked.Increment(ref delivered);
        };
        collector.Start();
        Until(() => provider.Reads >= 5 && Volatile.Read(ref delivered) >= 2,
            "A throwing subscriber stopped sampling or starved subsequent subscribers.");
        Require(collector.Latest.State == CollectorState.Running && collector.Latest.Reading.Received >= 400,
            "Canonical accounting did not progress after observer failure.");
    }

    private static void SlowObserver()
    {
        var provider = new ProbeProvider();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var collector = new Collector(provider, new StepClock()) { Interval = .5 };
        collector.Select("a");
        var blocked = 0;
        collector.Updated += snapshot =>
        {
            if (snapshot.State != CollectorState.Running || Interlocked.Exchange(ref blocked, 1) != 0) return;
            entered.Set();
            if (!release.Wait(8000)) throw new TimeoutException("Slow observer cleanup deadline.");
        };
        collector.Start();
        try
        {
            Require(entered.Wait(3000), "Running notification never arrived.");
            var before = provider.Reads;
            Until(() => provider.Reads >= before + 2, "Slow subscriber blocked the actual Start worker.", 2500);
            Require(collector.Latest.Reading.Received >= (ulong)(before + 1) * 100,
                "Canonical byte accounting did not advance while display observer blocked.");
        }
        finally { release.Set(); }
    }

    private static void CoalescedObserver()
    {
        var provider = new ProbeProvider();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var collector = new Collector(provider, new StepClock());
        collector.Select("a");
        collector.SampleOnce();
        var seen = new ConcurrentQueue<long>();
        var blocked = 0;
        collector.Updated += snapshot =>
        {
            if (snapshot.State != CollectorState.Running) return;
            seen.Enqueue(snapshot.Sequence);
            if (Interlocked.Exchange(ref blocked, 1) != 0) return;
            entered.Set();
            if (!release.Wait(8000)) throw new TimeoutException("Coalescing observer cleanup deadline.");
        };
        var first = Task.Run(collector.SampleOnce);
        try
        {
            Require(entered.Wait(3000), "First observer never entered.");
            Require(first.Wait(1000), "SampleOnce synchronously waited for a display observer.");
            for (var i = 0; i < 500; i++) collector.SampleOnce();
            var target = collector.Latest;
            Require(target.Reading.Received == 50100 && target.Reading.Sent == 25050,
                "Coalesced display delivery lost accounting deltas.");
            Require(seen.Count == 1, "Observer calls overlapped while the first delivery was blocked.");
            release.Set();
            Until(() => seen.Contains(target.Sequence), "Latest pending notification was not delivered.");
            Thread.Sleep(100);
            var deliveries = seen.ToArray();
            Require(deliveries.Length == 2 && deliveries[1] > deliveries[0],
                "Display retained/replayed intermediate snapshots instead of one latest pending snapshot.");
        }
        finally { release.Set(); }
    }

    private static void CallbackDisposal()
    {
        var provider = new ProbeProvider();
        using var returned = new ManualResetEventSlim();
        using var collector = new Collector(provider, new StepClock()) { Interval = .5 };
        collector.Select("a");
        var called = 0;
        collector.Updated += snapshot =>
        {
            if (snapshot.State != CollectorState.Running || Interlocked.Exchange(ref called, 1) != 0) return;
            collector.Dispose();
            returned.Set();
        };
        collector.Start();
        Require(returned.Wait(3500), "Dispose called from notification callback did not return.");
        Require(collector.Latest.State == CollectorState.Stopped && collector.Latest.Reading.Download is null,
            "Callback disposal failed to publish truthful stopped state.");
        var reads = provider.Reads;
        Thread.Sleep(650);
        Require(provider.Reads == reads, "Provider continued sampling after disposal returned.");
    }

    public static void Run(Action<string, Action> check)
    {
        check("AUDIT: throwing observer cannot stop actual sampling or other observers", () => RunChild("throw"));
        check("AUDIT: slow observer cannot block actual sampling worker", () => RunChild("slow"));
        check("AUDIT: one pending latest display snapshot preserves all accounting bytes", () => RunChild("coalesce"));
        check("AUDIT: Dispose from notification callback returns and stops sampling", () => RunChild("dispose"));

        foreach (var availability in new[] { ProviderAvailability.Partial, ProviderAvailability.Unsupported,
            ProviderAvailability.PermissionDenied, ProviderAvailability.TemporarilyUnavailable, ProviderAvailability.Failed })
        {
            check("AUDIT: typed provider state and recovery " + availability, () =>
            {
                var provider = new ProbeProvider();
                using var collector = new Collector(provider, new StepClock());
                collector.Select("a");
                collector.SampleOnce();
                var before = collector.SampleOnce();
                provider.Availability = availability;
                var unavailable = collector.SampleOnce();
                Require(unavailable.Observation.Availability == availability, "Provider status was erased.");
                Require(unavailable.State == (availability == ProviderAvailability.Failed ? CollectorState.Failed : CollectorState.Degraded),
                    "Wrong collector failure/degradation state.");
                Require(unavailable.Reading.Download is null && unavailable.Reading.Upload is null &&
                    unavailable.Reading.Received == before.Reading.Received, "Unavailable interval fabricated rates or bytes.");
                provider.Availability = ProviderAvailability.Available;
                var baseline = collector.SampleOnce();
                var live = collector.SampleOnce();
                Require(baseline.State == CollectorState.Starting && baseline.Reading.Received == 100 &&
                    live.State == CollectorState.Running && live.Reading.Received == 200,
                    "Recovery counted missing bytes or failed to rebaseline.");
            });
        }

        check("AUDIT: empty provider inventory preserves manual identity and rebaselines", () =>
        {
            var provider = new ProbeProvider();
            using var collector = new Collector(provider, new StepClock());
            collector.Select("a"); collector.SampleOnce(); collector.SampleOnce();
            provider.Empty = true;
            var empty = collector.SampleOnce();
            Require(empty.Observation.Availability == ProviderAvailability.Available && empty.Observation.Adapters.Count == 0,
                "Empty successful inventory lost provider semantics.");
            Require(empty.State == CollectorState.Degraded && empty.Reading.ScopeId == "a" && empty.Reading.Download is null,
                "Missing selected adapter silently switched or displayed zero.");
            provider.Empty = false;
            Require(collector.SampleOnce().State == CollectorState.Starting, "Returned interface did not rebaseline.");
            Require(collector.SampleOnce().Reading.Received == 200, "Empty interval was counted.");
        });

        check("AUDIT: suspend and pause are independent with no gap-byte reconstruction", () =>
        {
            var provider = new ProbeProvider();
            using var collector = new Collector(provider, new StepClock());
            collector.Select("a"); collector.SampleOnce(); collector.SampleOnce();
            collector.TogglePause(); collector.SetSuspended(true); collector.SetSuspended(false);
            Require(collector.Paused && collector.Latest.State == CollectorState.Paused, "Resume cancelled explicit pause.");
            var reads = provider.Reads;
            collector.SampleOnce();
            Require(provider.Reads == reads, "Paused collector still queried provider.");
            collector.TogglePause();
            Require(collector.SampleOnce().State == CollectorState.Starting, "Resume lacks new baseline.");
            Require(collector.SampleOnce().Reading.Received == 200, "Pause/suspend gap became measured bytes.");
        });

        check("AUDIT: invalid monotonic clock cannot publish Running and recovery rebaselines", () =>
        {
            var provider = new ProbeProvider(); var clock = new ManualClock();
            using var collector = new Collector(provider, clock);
            collector.Select("a"); collector.SampleOnce();
            clock.Seconds = double.NaN;
            var invalid = collector.SampleOnce();
            Require(invalid.State == CollectorState.Degraded && invalid.Reading.State == ReadingState.Discontinuity &&
                invalid.Reading.MonotonicSeconds is null && invalid.Reading.Received == 0, "Invalid clock reached live accounting.");
            clock.Seconds = 1;
            Require(collector.SampleOnce().State == CollectorState.Starting, "Clock recovery lacks baseline.");
            clock.Seconds = 2;
            Require(collector.SampleOnce().Reading.Received == 100, "Invalid clock span entered accounting.");
        });

        check("AUDIT: invalid interval configuration is rejected without replacing prior interval", () =>
        {
            using var collector = new Collector(new ProbeProvider(), new StepClock());
            foreach (var value in new[] { 0d, -1d, .25, 3, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var rejected = false;
                try { collector.Interval = value; } catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected && collector.Interval == 1, "Invalid interval was accepted or mutated configuration.");
            }
        });

        check("AUDIT: interruption cannot create Live state with missing rates", () =>
        {
            var rejected = false;
            try { new MeasurementEngine().Interrupt(Utc, ReadingState.Live, "Invalid caller state"); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Interrupt accepted Live, violating valid-reading invariant.");
        });

        check("AUDIT: published provider inventory is a defensive immutable snapshot", () =>
        {
            var original = new AdapterCounter("a", "Original", "Fixture", true, 10, 20, 0);
            var source = new List<AdapterCounter> { original };
            var observation = new AdapterObservation(ProviderAvailability.Available, source, "Fixture", "ok", "Fixture");
            source[0] = original with { Received = 999, Name = "Changed" }; source.Clear();
            Require(observation.Adapters.Count == 1 && observation.Adapters[0] == original,
                "Provider mutation changed published inventory.");
            if (observation.Adapters is IList<AdapterCounter> mutable)
            {
                var rejected = false;
                try { mutable[0] = original with { Received = 999 }; } catch (NotSupportedException) { rejected = true; }
                Require(rejected, "Read-only inventory can be mutated through IList.");
            }
        });

        check("AUDIT: past canonical snapshot survives subsequent accounting and reset", () =>
        {
            var provider = new ProbeProvider();
            using var collector = new Collector(provider, new StepClock());
            collector.Select("a"); collector.SampleOnce();
            var prior = collector.SampleOnce(); var id = prior.Reading.SnapshotId; var sequence = prior.Sequence;
            collector.SampleOnce(); collector.Reset();
            Require(prior.Reading.Received == 100 && prior.Reading.Sent == 50 && prior.Reading.SnapshotId == id &&
                prior.Sequence == sequence && prior.Observation.Adapters[0].Received == 200,
                "Later accounting mutated a published canonical snapshot.");
            Require(collector.Latest.Reading.Received == 0 && collector.Latest.Reading.SessionId != prior.Reading.SessionId,
                "Reset failed to create distinct session state.");
        });
    }
}
