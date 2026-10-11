using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Windows;

namespace SpeedMeter.Checks;

public static class PerfFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("PERF: preliminary adapter latency and retained heap observation", () =>
        {
            var provider = new AdapterProvider();

            // Warm up
            _ = provider.ReadSnapshot();

            const int iterations = 50;
            var memBefore = GC.GetTotalMemory(true);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < iterations; i++)
            {
                var snap = provider.ReadSnapshot();
                Require(snap.Availability == ProviderAvailability.Available, "Snapshot failed");
            }

            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);
            var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Console.WriteLine($"  ReadSnapshot avg latency: {avgMs:F3} ms over {iterations} immediate calls; calling-thread allocation: {allocatedBytes} bytes; retained heap delta: {memAfter - memBefore} bytes. Not a PRD resource-budget test.");
            Require(avgMs < 20.0, $"ReadSnapshot took too long: {avgMs:F2} ms (budget: 20 ms)");
        });

        check("PERF: preliminary headless SampleOnce average latency", () =>
        {
            var provider = new AdapterProvider();
            var clock = new MeasurementClock();
            using var collector = new Collector(provider, clock);

            // Select active primary adapter if available
            var initial = provider.ReadSnapshot();
            var up = initial.Adapters.FirstOrDefault(a => a.IsUp);
            if (up is not null) collector.Select(up.Id);

            // Warm up
            _ = collector.SampleOnce();

            var sw = Stopwatch.StartNew();
            const int iterations = 50;

            for (var i = 0; i < iterations; i++)
            {
                var snap = collector.SampleOnce();
                Require(snap.State is CollectorState.Running or CollectorState.Starting or CollectorState.Degraded, "Unexpected collector state");
            }

            sw.Stop();
            var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            Console.WriteLine($"  Collector SampleOnce avg latency: {avgMs:F3} ms over {iterations} iterations.");
            Require(avgMs < 25.0, $"SampleOnce exceeded latency budget: {avgMs:F2} ms (budget: 25 ms)");
        });

        check("PERF: 100 immediate samples and test-process working-set ceiling", () =>
        {
            var provider = new AdapterProvider();
            var clock = new MeasurementClock();
            using var collector = new Collector(provider, clock);

            var initialSnap = provider.ReadSnapshot();
            var up = initialSnap.Adapters.FirstOrDefault(a => a.IsUp);
            if (up is not null) collector.Select(up.Id);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var memStart = GC.GetTotalMemory(true);

            // Immediate observations: no real-time Start loop or soak claim.
            for (var i = 0; i < 100; i++)
            {
                collector.SampleOnce();
            }

            var memEnd = GC.GetTotalMemory(false);
            var memGrowth = memEnd - memStart;

            using var proc = Process.GetCurrentProcess();
            Console.WriteLine($"  Process Working Set: {proc.WorkingSet64 / 1024 / 1024} MB, Thread Count: {proc.Threads.Count}, Handle Count: {proc.HandleCount}.");
            Console.WriteLine($"  Heap memory delta over 100 samples: {memGrowth / 1024} KB.");

            Require(proc.WorkingSet64 < 300 * 1024 * 1024, "Working set exceeded 300 MB budget");
        });
    }
}
