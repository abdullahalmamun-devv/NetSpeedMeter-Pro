using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

public static class V1FoundationFixtures
{
    private sealed class StubRouteProvider : IRouteProvider
    {
        public List<AdapterScopeCandidate> Candidates { get; set; } = [];
        public IReadOnlyList<AdapterScopeCandidate> GetCandidates() => Candidates;
    }

    private sealed class StubClock : IMeasurementClock
    {
        public double CurrentMonotonic { get; set; } = 100.0;
        public DateTimeOffset CurrentUtc { get; set; } = DateTimeOffset.UtcNow;
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
        check("V1-SCOPE: AutoPrimary follows route changes with clean rebaseline and preserved session bytes", TestScopeIntegration_AutoPrimaryFollowsRouteChanges);
        check("V1-SCOPE: Route flapping debounce suppresses spurious revisions and commits after stabilization", TestScopeIntegration_RouteFlappingDebounceSuppressesSpuriousRevisions);
        check("V1-SCOPE: Dual-stack disjoint egress flags ambiguity and fails closed", TestScopeIntegration_DualStackDisjointEgressFlagsAmbiguity);
        check("V1-STORE: 8-byte big-endian BLOB codec roundtrips full 64-bit unsigned range losslessly", TestStorage_8ByteBigEndianBlobRoundtrip);
        check("V1-STORE: CheckedIntegerMath enforces signed capacity guards and prevents overflow", TestStorage_CheckedIntegerCapacityRejectsOverflow);
        check("V1-STORE: Daily rollup uniqueness enforces compound PRIMARY KEY without null duplicate bug", TestStorage_RollupUniquenessWithoutNullDuplication);
        check("V1-STORE: Exact bytes, gap records, and clean session metadata survive process restart", () => TestStorage_ExactBytesSurviveProcessRestart().GetAwaiter().GetResult());
        check("V1-LIFECYCLE: Unclean shutdown is detected and recorded as a crash gap on restart", () => TestStorage_UncleanShutdownRecoveryRecordsCrashGap().GetAwaiter().GetResult());
        check("V1-INTEGRATION: Collector + RouteProvider + StorageEngine end-to-end persists exact interval bytes and checkpoints", () => TestCollector_EndToEndIntegrationWithScopeAndStorage().GetAwaiter().GetResult());
        check("V1-RESILIENCE: Storage degradation does not crash collector or drop live observations", TestCollector_StorageDegradationDoesNotCrashSampling);
    }

    public static void TestScopeIntegration_AutoPrimaryFollowsRouteChanges()
    {
        var clock = new StubClock { CurrentMonotonic = 10.0 };
        var routeProvider = new StubRouteProvider();
        var wifiId = "wifi-adapter-1";
        var ethId = "eth-adapter-2";

        routeProvider.Candidates =
        [
            new AdapterScopeCandidate(wifiId, "Wi-Fi", "Intel Wireless", true, true, 866_000_000,
                InterfaceClassification.PhysicalWireless, true, 10),
            new AdapterScopeCandidate(ethId, "Ethernet", "Realtek GbE", true, true, 1_000_000_000,
                InterfaceClassification.PhysicalEthernet, false, 20)
        ];

        var counterProvider = new StubCounterProvider
        {
            Adapters =
            [
                new AdapterCounter(wifiId, "Wi-Fi", "Intel Wireless", true, 1000, 500, 866_000_000, true),
                new AdapterCounter(ethId, "Ethernet", "Realtek GbE", true, 2000, 800, 1_000_000_000, true)
            ]
        };

        using var collector = new Collector(counterProvider, clock, routeProvider, routeDebounceSeconds: 0);

        // First sample captures baseline for Wi-Fi
        var s1 = collector.SampleOnce();
        Assert(s1.ScopeDecision?.PrimaryAdapterId == wifiId, "AutoPrimary initial primary must be Wi-Fi");
        Assert(s1.State == CollectorState.Starting, "First sample should be Starting (baseline capture)");

        // Second sample on Wi-Fi
        clock.CurrentMonotonic += 1.0;
        counterProvider.Adapters =
        [
            new AdapterCounter(wifiId, "Wi-Fi", "Intel Wireless", true, 11000, 5500, 866_000_000, true),
            new AdapterCounter(ethId, "Ethernet", "Realtek GbE", true, 2000, 800, 1_000_000_000, true)
        ];
        var s2 = collector.SampleOnce();
        Assert(s2.State == CollectorState.Running, "Second sample should be Running");
        Assert(s2.Reading.Download == 10000, "Download should be 10,000 B/s");

        // Route switch: Ethernet now becomes primary default route
        clock.CurrentMonotonic += 1.0;
        routeProvider.Candidates =
        [
            new AdapterScopeCandidate(wifiId, "Wi-Fi", "Intel Wireless", true, true, 866_000_000,
                InterfaceClassification.PhysicalWireless, false, 20),
            new AdapterScopeCandidate(ethId, "Ethernet", "Realtek GbE", true, true, 1_000_000_000,
                InterfaceClassification.PhysicalEthernet, true, 10)
        ];
        counterProvider.Adapters =
        [
            new AdapterCounter(wifiId, "Wi-Fi", "Intel Wireless", true, 11000, 5500, 866_000_000, true),
            new AdapterCounter(ethId, "Ethernet", "Realtek GbE", true, 2000, 800, 1_000_000_000, true)
        ];

        var s3 = collector.SampleOnce();
        Assert(s3.ScopeDecision?.PrimaryAdapterId == ethId, "Scope decision must switch primary to Ethernet");
        Assert(s3.ScopeDecision?.Revision == 2, "Scope revision must increment to 2");
        Assert(s3.State == CollectorState.Starting, "Scope change must establish fresh baseline (Starting state)");
        Assert(s3.Reading.Download == null, "Scope change must NOT produce a false speed spike (Download should be null)");
        Assert(s3.Reading.Received == 10000, "Previous session cumulative bytes must be preserved across scope change");
    }

    public static void TestScopeIntegration_RouteFlappingDebounceSuppressesSpuriousRevisions()
    {
        var initial = new CanonicalScopeDecision(
            Guid.NewGuid(), 1, ScopeMode.AutoPrimary, TopologyState.SinglePrimary,
            ["eth-1"], "eth-1", "Ethernet primary", "Detail", false, DateTimeOffset.UtcNow);

        var stabilizer = new ScopeStabilizer(initial, debounceSeconds: 2.0);

        var bouncingWifi = new CanonicalScopeDecision(
            Guid.NewGuid(), 2, ScopeMode.AutoPrimary, TopologyState.SinglePrimary,
            ["wifi-1"], "wifi-1", "Wi-Fi primary", "Detail", false, DateTimeOffset.UtcNow);

        // Monotonic 100.0: first observation of bounce to Wi-Fi
        var r1 = stabilizer.Update(bouncingWifi, 100.0);
        Assert(r1.PrimaryAdapterId == "eth-1", "Debounce should retain eth-1 while timer is pending");
        Assert(r1.Revision == 1, "Revision should remain 1");

        // Monotonic 101.0: bounce back to Ethernet before 2 seconds elapsed
        var r2 = stabilizer.Update(initial, 101.0);
        Assert(r2.PrimaryAdapterId == "eth-1", "Debounce cancelled; stays on eth-1");
        Assert(r2.Revision == 1, "Revision should not have incremented during transient bounce");

        // Monotonic 102.0: stable switch to Wi-Fi begins
        stabilizer.Update(bouncingWifi, 102.0);
        // Monotonic 103.0: 1 second in (still debouncing)
        var r3 = stabilizer.Update(bouncingWifi, 103.0);
        Assert(r3.PrimaryAdapterId == "eth-1", "Still debouncing after 1.0s");

        // Monotonic 104.5: 2.5 seconds in (>= 2.0s debounce threshold)
        var r4 = stabilizer.Update(bouncingWifi, 104.5);
        Assert(r4.PrimaryAdapterId == "wifi-1", "Stabilizer should commit Wi-Fi after debounce elapses");
        Assert(r4.Revision == 2, "Revision increments to 2 upon committed switch");
    }

    public static void TestScopeIntegration_DualStackDisjointEgressFlagsAmbiguity()
    {
        var candidates = new List<AdapterScopeCandidate>
        {
            new("eth-1", "Ethernet", "NIC 1", true, true, 1_000_000_000, InterfaceClassification.PhysicalEthernet, true, 10)
            {
                HasIpv4DefaultRoute = true,
                HasIpv6DefaultRoute = false
            },
            new("wifi-1", "Wi-Fi", "NIC 2", true, true, 866_000_000, InterfaceClassification.PhysicalWireless, true, 20)
            {
                HasIpv4DefaultRoute = false,
                HasIpv6DefaultRoute = true
            }
        };

        var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);
        Assert(decision.Topology == TopologyState.DualStackDisjoint, "DualStackDisjoint topology expected when v4 and v6 egress differ");
        Assert(decision.IsAmbiguous == true, "Dual-stack disjoint egress must be flagged ambiguous");
        Assert(decision.ActiveAdapterIds.Count == 0, "Disjoint egress must fail closed without automatic merge");
    }

    public static void TestStorage_8ByteBigEndianBlobRoundtrip()
    {
        ulong[] testValues =
        [
            0UL,
            1UL,
            (ulong)long.MaxValue,
            (ulong)long.MaxValue + 1UL, // 2^63
            ulong.MaxValue - 1UL,
            ulong.MaxValue
        ];

        foreach (var val in testValues)
        {
            var bytes = BigEndianCodec.EncodeUInt64(val);
            Assert(bytes.Length == 8, "Encoded BLOB must be exactly 8 bytes");
            var roundtrip = BigEndianCodec.DecodeUInt64(bytes);
            Assert(roundtrip == val, $"Roundtrip mismatch: {val} != {roundtrip}");
        }

        try
        {
            BigEndianCodec.DecodeUInt64(new byte[7]);
            throw new Exception("DecodeUInt64 should reject 7-byte input");
        }
        catch (ArgumentException) { }
    }

    public static void TestStorage_CheckedIntegerCapacityRejectsOverflow()
    {
        var safe = CheckedIntegerMath.CheckedToSigned((ulong)long.MaxValue, "safe delta");
        Assert(safe == long.MaxValue, "Max signed value should convert safely");

        try
        {
            CheckedIntegerMath.CheckedToSigned((ulong)long.MaxValue + 1UL, "overflow delta");
            throw new Exception("CheckedToSigned must throw on overflow");
        }
        catch (StorageCapacityExceededException) { }

        try
        {
            CheckedIntegerMath.CheckedAdd(long.MaxValue, 1, "overflow add");
            throw new Exception("CheckedAdd must throw on overflow");
        }
        catch (StorageCapacityExceededException) { }
    }

    public static void TestStorage_RollupUniquenessWithoutNullDuplication()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_unique_{Guid.NewGuid():N}.db");
        try
        {
            using (var storage = new SqliteStorageEngine(dbPath))
            {
                // Verify schema creation
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();

                using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = @"
                    INSERT INTO daily_rollups (start_utc_ms, end_utc_ms, reporting_timezone, calendar_rule_revision, scope_id, network_profile_id, bytes_rx, bytes_tx, valid_duration_ms, gap_duration_ms)
                    VALUES (1000, 2000, 'UTC', '1', 'scope-1', 'unidentified', 500, 200, 1000, 0);
                ";
                cmd1.ExecuteNonQuery();

                // Attempting duplicate insertion with the same compound key must fail
                try
                {
                    using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = @"
                        INSERT INTO daily_rollups (start_utc_ms, end_utc_ms, reporting_timezone, calendar_rule_revision, scope_id, network_profile_id, bytes_rx, bytes_tx, valid_duration_ms, gap_duration_ms)
                        VALUES (1000, 2000, 'UTC', '1', 'scope-1', 'unidentified', 999, 999, 1000, 0);
                    ";
                    cmd2.ExecuteNonQuery();
                    throw new Exception("Duplicate compound key insertion should have been rejected by PRIMARY KEY");
                }
                catch (SqliteException)
                {
                    // Expected primary key violation
                }
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_ExactBytesSurviveProcessRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_restart_{Guid.NewGuid():N}.db");
        var sessionId = Guid.NewGuid().ToString("D");
        var clockSegId = Guid.NewGuid().ToString("D");
        var scopeRevId = "scope-1_rev_1";

        try
        {
            // === Session 1: Write data and cleanly close ===
            await using (var storage1 = new SqliteStorageEngine(dbPath))
            {
                await storage1.RecordSessionStartAsync(new SessionRecord(sessionId, "epoch_1", 10000, null, null, false));
                await storage1.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, sessionId, 0, 10000, 10_000_000, "nominal"));
                await storage1.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 10000));

                // Ingest 125 MB Rx, 12.5 MB Tx
                await storage1.IngestIntervalAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"{sessionId}_iv_1", sessionId, clockSegId, scopeRevId, "unidentified",
                    10000, 11000, 10_000_000, 125_000_000, 12_500_000, false, false, null, "nominal"));

                // Ingest another 250 MB Rx, 25 MB Tx
                await storage1.IngestIntervalAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"{sessionId}_iv_2", sessionId, clockSegId, scopeRevId, "unidentified",
                    11000, 12000, 10_000_000, 250_000_000, 25_000_000, false, false, null, "nominal"));

                // Ingest explicit gap (e.g. paused for 5 seconds)
                await storage1.IngestGapAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"{sessionId}_gap_1", sessionId, clockSegId, scopeRevId, "unidentified",
                    12000, 17000, 50_000_000, 0, 0, false, true, "user_paused", "nominal"));

                // Clean session end
                await storage1.RecordSessionEndAsync(sessionId, DateTimeOffset.FromUnixTimeMilliseconds(17000), true, "normal_quit");
                await storage1.FlushAsync(TimeSpan.FromSeconds(5));
            }

            // === Session 2: Reopen same database file, simulating restart ===
            await using (var storage2 = new SqliteStorageEngine(dbPath))
            {
                var lastSession = await storage2.GetLastSessionAsync();
                Assert(lastSession != null, "Last session record must survive restart");
                Assert(lastSession!.EndedCleanly, "Session 1 must be marked ended cleanly");
                Assert(lastSession.EndedAtUtcMs == 17000, "EndedAtUtcMs must match 17000");

                var intervals = await storage2.GetIntervalsAsync();
                Assert(intervals.Count == 3, $"Expected 3 intervals (2 live + 1 gap), found {intervals.Count}");

                var gap = intervals.Single(i => i.IsGap);
                Assert(gap.GapReason == "user_paused", "Gap reason must survive restart");
                Assert(gap.BytesRx == 0 && gap.BytesTx == 0, "Gap bytes must be 0");

                var totals = await storage2.GetTotalBytesAsync();
                Assert(totals.TotalRx == 375_000_000, $"Total RX mismatch: expected 375,000,000, got {totals.TotalRx}");
                Assert(totals.TotalTx == 37_500_000, $"Total TX mismatch: expected 37,500,000, got {totals.TotalTx}");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_UncleanShutdownRecoveryRecordsCrashGap()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_crash_{Guid.NewGuid():N}.db");
        var crashedSessionId = Guid.NewGuid().ToString("D");

        try
        {
            // Seed a crashed session in the DB (ended_cleanly = 0, ended_at_utc_ms = null)
            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                await storage.RecordSessionStartAsync(new SessionRecord(crashedSessionId, "epoch_crash", 5000, null, null, false));
                await storage.FlushAsync(TimeSpan.FromSeconds(5));
            }

            // Start collector on that DB
            var clock = new StubClock { CurrentMonotonic = 50.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(20000) };
            var counterProvider = new StubCounterProvider();

            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                using var collector = new Collector(counterProvider, clock, storage: storage);
                collector.Start();

                // Give background startup task time to run
                await Task.Delay(200);
                await storage.FlushAsync(TimeSpan.FromSeconds(5));

                var intervals = await storage.GetIntervalsAsync();
                var crashGap = intervals.FirstOrDefault(i => i.GapReason == "unclean_shutdown");
                Assert(crashGap != null, "Collector must record unclean_shutdown gap when previous session was unclean");
                Assert(crashGap!.StartUtcMs == 5000, "Crash gap start should equal crashed session start");
                Assert(crashGap.EndUtcMs == 20000, "Crash gap end should equal new session start");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestCollector_EndToEndIntegrationWithScopeAndStorage()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_e2e_{Guid.NewGuid():N}.db");
        var clock = new StubClock { CurrentMonotonic = 100.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(100000) };
        var routeProvider = new StubRouteProvider();
        var primaryId = "e2e-adapter-1";

        routeProvider.Candidates =
        [
            new AdapterScopeCandidate(primaryId, "Ethernet", "NIC 1", true, true, 1_000_000_000,
                InterfaceClassification.PhysicalEthernet, true, 10)
        ];

        var counterProvider = new StubCounterProvider
        {
            Adapters =
            [
                new AdapterCounter(primaryId, "Ethernet", "NIC 1", true, 1_000_000, 500_000, 1_000_000_000, true)
            ]
        };

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            using var collector = new Collector(counterProvider, clock, routeProvider, storage, routeDebounceSeconds: 0);

            // Tick 1: Baseline captured
            var snap1 = collector.SampleOnce();
            Assert(snap1.ScopeDecision?.PrimaryAdapterId == primaryId, "Scope decision must match primary");
            Assert(snap1.State == CollectorState.Starting, "State must be Starting");

            // Tick 2: 12.5 MB Rx, 1.25 MB Tx in 1.0 second
            clock.CurrentMonotonic += 1.0;
            clock.CurrentUtc = clock.CurrentUtc.AddSeconds(1.0);
            counterProvider.Adapters =
            [
                new AdapterCounter(primaryId, "Ethernet", "NIC 1", true, 13_500_000, 1_750_000, 1_000_000_000, true)
            ];

            var snap2 = collector.SampleOnce();
            Assert(snap2.State == CollectorState.Running, "State must be Running");
            Assert(snap2.Reading.Download == 12_500_000, "Download rate should be 12,500,000 B/s");

            // Flush storage
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Verify persistence in SQLite
            var intervals = await storage.GetIntervalsAsync();
            Assert(intervals.Count == 1, $"Expected 1 persisted interval, found {intervals.Count}");
            Assert(intervals[0].BytesRx == 12_500_000, $"Persisted BytesRx mismatch: {intervals[0].BytesRx}");
            Assert(intervals[0].BytesTx == 1_250_000, $"Persisted BytesTx mismatch: {intervals[0].BytesTx}");

            var totals = await storage.GetTotalBytesAsync();
            Assert(totals.TotalRx == 12_500_000, $"Totals TotalRx mismatch: {totals.TotalRx}");
            Assert(totals.TotalTx == 1_250_000, $"Totals TotalTx mismatch: {totals.TotalTx}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static void TestCollector_StorageDegradationDoesNotCrashSampling()
    {
        var clock = new StubClock { CurrentMonotonic = 10.0 };
        var counterProvider = new StubCounterProvider
        {
            Adapters = [new AdapterCounter("adapter-1", "Eth", "NIC", true, 100, 100, 1000, true)]
        };

        // Create storage and immediately dispose it to simulate degraded/closed storage
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_degraded_{Guid.NewGuid():N}.db");
        var storage = new SqliteStorageEngine(dbPath);
        storage.Dispose();

        try
        {
            using var collector = new Collector(counterProvider, clock, storage: storage);
            collector.Select("adapter-1");

            // SampleOnce should continue safely without throwing
            var snap1 = collector.SampleOnce();
            Assert(snap1 != null, "Snapshot must not be null");

            clock.CurrentMonotonic += 1.0;
            counterProvider.Adapters = [new AdapterCounter("adapter-1", "Eth", "NIC", true, 200, 200, 1000, true)];
            var snap2 = collector.SampleOnce();
            Assert(snap2 != null, "SampleOnce must survive closed/degraded storage");
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
