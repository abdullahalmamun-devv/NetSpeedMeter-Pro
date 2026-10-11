using System;
using System.Collections.Generic;
using System.Linq;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

/// <summary>
/// Independent QA verification fixtures covering Application Collector lifecycle,
/// failure resilience, and boundary invariants.
/// </summary>
public static class QaFixtures
{
    private sealed class TestClock : IMeasurementClock
    {
        public double CurrentMonotonic { get; set; }
        public DateTimeOffset CurrentUtc { get; set; } = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        public void Advance(double seconds)
        {
            CurrentMonotonic += seconds;
            CurrentUtc = CurrentUtc.AddSeconds(seconds);
        }

        public ClockReading Read() => new(CurrentMonotonic, CurrentUtc);
    }

    private sealed class TestProvider : IAdapterCounterProvider
    {
        public ProviderAvailability Availability { get; set; } = ProviderAvailability.Available;
        public List<AdapterCounter> Adapters { get; set; } = [];
        public string Method { get; set; } = "TestFixture";
        public string Code { get; set; } = "ok";
        public string Detail { get; set; } = "Test adapter snapshot.";
        public bool ThrowOnRead { get; set; }

        public AdapterObservation ReadSnapshot()
        {
            if (ThrowOnRead) throw new InvalidOperationException("Simulated provider hardware failure.");
            return new AdapterObservation(Availability, Adapters, Method, Code, Detail);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("QA: Collector starts in Starting state with zero readings", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            using var collector = new Collector(provider, clock);

            Require(collector.Latest.State == CollectorState.Starting, "Collector did not start in Starting state.");
            Require(collector.Latest.Sequence == 0, "Initial sequence must be zero.");
            Require(collector.Latest.Reading.State == ReadingState.Measuring, "Initial reading state must be Measuring.");
        });

        check("QA: Scope selection captures baseline, then establishes Running state", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            provider.Adapters.Add(new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 1000, 500, 1_000_000_000, true));

            using var collector = new Collector(provider, clock);
            collector.Select("nic1");

            // First sample captures baseline
            var s1 = collector.SampleOnce();
            Require(s1.State == CollectorState.Starting, "Baseline sample must be in Starting state.");
            Require(s1.Reading.Download is null, "Baseline sample must not report rate.");
            Require(s1.Reading.Received == 0, "Baseline sample must not accumulate session bytes.");

            // Advance time and counters
            clock.Advance(1.0);
            provider.Adapters[0] = new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 2000, 1000, 1_000_000_000, true);
            var s2 = collector.SampleOnce();

            Require(s2.State == CollectorState.Running, "Active sample must transition to Running.");
            Require(s2.Reading.IsValid, "Reading must be valid.");
            Require(s2.Reading.Download == 1000.0, $"Download rate expected 1000, got {s2.Reading.Download}.");
            Require(s2.Reading.Upload == 500.0, $"Upload rate expected 500, got {s2.Reading.Upload}.");
            Require(s2.Reading.Received == 1000, "Received total mismatch.");
            Require(s2.Reading.Sent == 500, "Sent total mismatch.");
        });

        check("QA: Collector pause excludes interval and preserves prior session totals", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            provider.Adapters.Add(new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 1000, 500, 1_000_000_000, true));

            using var collector = new Collector(provider, clock);
            collector.Select("nic1");
            collector.SampleOnce();

            clock.Advance(1.0);
            provider.Adapters[0] = new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 3000, 1500, 1_000_000_000, true);
            var active = collector.SampleOnce();
            Require(active.Reading.Received == 2000, "Pre-pause total mismatch.");

            // Pause
            collector.TogglePause();
            Require(collector.Paused, "Collector must be paused.");
            Require(collector.Latest.State == CollectorState.Paused, "Latest state must be Paused.");

            // Traffic occurs while paused
            clock.Advance(5.0);
            provider.Adapters[0] = new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 100_000, 50_000, 1_000_000_000, true);
            collector.SampleOnce();

            // Resume
            collector.TogglePause();
            Require(!collector.Paused, "Collector must not be paused.");

            // First sample after resume captures new baseline; does not count unobserved paused traffic
            clock.Advance(1.0);
            var rebaseline = collector.SampleOnce();
            Require(rebaseline.Reading.Received == 2000, "Baseline capture must not alter prior session total.");
            Require(rebaseline.Reading.Download is null, "Baseline sample must not report rate.");

            // Subsequent sample produces first live delta after resume
            clock.Advance(1.0);
            provider.Adapters[0] = new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 100_500, 50_250, 1_000_000_000, true);
            var resumed = collector.SampleOnce();

            Require(resumed.Reading.Received == 2500, $"Total mismatch! Expected 2500, got {resumed.Reading.Received}");
            Require(resumed.Reading.Sent == 1250, $"Total mismatch! Expected 1250, got {resumed.Reading.Sent}");
            Require(resumed.Reading.Download == 500.0, $"Rate mismatch: {resumed.Reading.Download}");
        });

        check("QA: Provider failure transitions to Failed state without throwing", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            provider.Adapters.Add(new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 1000, 500, 1_000_000_000, true));

            using var collector = new Collector(provider, clock);
            collector.Select("nic1");
            collector.SampleOnce();

            // Simulate provider exception
            provider.ThrowOnRead = true;
            var failed = collector.SampleOnce();

            Require(failed.State == CollectorState.Failed, "Collector must enter Failed state on provider error.");
            Require(failed.Reading.State == ReadingState.Unavailable, "Reading state must be Unavailable.");
            Require(failed.Reading.Download is null, "Rate must be null during failure.");

            // Recover provider
            provider.ThrowOnRead = false;
            clock.Advance(1.0);
            var recovered = collector.SampleOnce();

            // Should rebaseline after failure
            Require(recovered.State == CollectorState.Starting, "Collector must rebaseline after recovery.");
        });

        check("QA: Provider typed degradation (PermissionDenied/TemporarilyUnavailable) is preserved", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider { Availability = ProviderAvailability.PermissionDenied, Detail = "Access denied." };

            using var collector = new Collector(provider, clock);
            var snapshot = collector.SampleOnce();

            Require(snapshot.State == CollectorState.Degraded, "Collector should be Degraded on PermissionDenied.");
            Require(snapshot.Observation.Availability == ProviderAvailability.PermissionDenied, "Observation availability lost.");
        });

        check("QA: Collector Reset cleanly clears session counters and coverage", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            provider.Adapters.Add(new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 1000, 500, 1_000_000_000, true));

            using var collector = new Collector(provider, clock);
            collector.Select("nic1");
            collector.SampleOnce();

            clock.Advance(1.0);
            provider.Adapters[0] = new AdapterCounter("nic1", "Ethernet 1", "Realtek", true, 2000, 1000, 1_000_000_000, true);
            collector.SampleOnce();

            collector.Reset();
            Require(collector.Latest.Reading.Received == 0, "Received total not reset.");
            Require(collector.Latest.Reading.Sent == 0, "Sent total not reset.");
            Require(collector.Latest.Reading.CoveredSeconds == 0, "Coverage not reset.");
        });

        check("QA: Sequence monotonically increments on each snapshot", () =>
        {
            var clock = new TestClock();
            var provider = new TestProvider();
            using var collector = new Collector(provider, clock);

            var seq0 = collector.Latest.Sequence;
            collector.Select("test");
            var seq1 = collector.Latest.Sequence;
            collector.SampleOnce();
            var seq2 = collector.Latest.Sequence;
            collector.TogglePause();
            var seq3 = collector.Latest.Sequence;

            Require(seq1 > seq0 && seq2 > seq1 && seq3 > seq2, "Sequence did not strictly increment.");
        });
    }
}
