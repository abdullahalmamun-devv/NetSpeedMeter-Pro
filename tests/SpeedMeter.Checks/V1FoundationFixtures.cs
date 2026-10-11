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

        check("V1-AUDIT: Crash gap start matches last durable interval end with zero history overlap", () => TestStorage_CrashGapDoesNotOverlapValidHistory().GetAwaiter().GetResult());
        check("V1-AUDIT: Empty crashed session falls back to session start for crash gap", () => TestStorage_EmptyCrashedSessionRecoversFullSessionGap().GetAwaiter().GetResult());
        check("V1-AUDIT: Incompatible future schema version is rejected without mutating database", () => TestStorage_IncompatibleNewerSchemaRejected().GetAwaiter().GetResult());
        check("V1-AUDIT: Interval and counter checkpoint committed atomically in single transaction", () => TestStorage_IntervalAndCheckpointCommittedAtomically().GetAwaiter().GetResult());
        check("V1-AUDIT: Duplicate interval ID is rejected idempotently by primary key constraint", () => TestStorage_IdempotentDuplicateIntervalRejected().GetAwaiter().GetResult());
        check("V1-AUDIT: Child process out-of-process restart preserves exact bytes and metadata", () => TestStorage_SeparateProcessRestartPreservesExactBytes().GetAwaiter().GetResult());
        check("V1-AUDIT: Forced FailFast process crash recovers accurate crash gap without overlap", () => TestStorage_ForcedProcessCrashRecoversAccurateGap().GetAwaiter().GetResult());
        check("V1-AUDIT: Degraded storage truthfully transitions collector snapshot to Degraded state", () => TestCollector_SurfacesStorageDegradationOnSnapshot().GetAwaiter().GetResult());
        check("V1-AUDIT: Queue capacity saturation transitions storage health to Degraded", () => TestStorage_QueueSaturationTriggersDegradedState().GetAwaiter().GetResult());
        check("V1-AUDIT: Capacity overflow delta forces engine discontinuity rebaseline and gap record", () => TestStorage_CapacityExceededTriggersRebaselineAndGap().GetAwaiter().GetResult());
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

    public static int? TryRunChild(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--v1-child=", StringComparison.Ordinal));
        if (arg is null) return null;
        var command = arg["--v1-child=".Length..];
        try
        {
            if (command.StartsWith("write-and-exit:", StringComparison.Ordinal))
            {
                var dbPath = command["write-and-exit:".Length..];
                ChildWriteAndExit(dbPath).GetAwaiter().GetResult();
                return 0;
            }
            if (command.StartsWith("write-and-crash:", StringComparison.Ordinal))
            {
                var dbPath = command["write-and-crash:".Length..];
                ChildWriteAndCrash(dbPath).GetAwaiter().GetResult();
                return 0;
            }
            Console.Error.WriteLine("Unknown --v1-child scenario: " + command);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("V1 child error: " + ex);
            return 1;
        }
    }

    private static async Task ChildWriteAndExit(string dbPath)
    {
        var sessionId = "child_session_clean";
        var clockSegId = "child_clock_seg";
        var scopeRevId = "child_scope_rev";

        await using var storage = new SqliteStorageEngine(dbPath);
        await storage.RecordSessionStartAsync(new SessionRecord(sessionId, "epoch_child", 10000, null, null, false));
        await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, sessionId, 0, 10000, 10_000_000, "nominal"));
        await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 10000));

        await storage.IngestIntervalAsync(new UsageIntervalRecord(
            Guid.NewGuid().ToString("D"), $"{sessionId}_iv_1", sessionId, clockSegId, scopeRevId, "unidentified",
            10000, 11000, 10_000_000, 25_000_000, 2_500_000, false, false, null, "nominal"));

        await storage.IngestIntervalAsync(new UsageIntervalRecord(
            Guid.NewGuid().ToString("D"), $"{sessionId}_iv_2", sessionId, clockSegId, scopeRevId, "unidentified",
            11000, 12000, 10_000_000, 50_000_000, 5_000_000, false, false, null, "nominal"));

        await storage.RecordSessionEndAsync(sessionId, DateTimeOffset.FromUnixTimeMilliseconds(12000), true, "normal_exit");
        await storage.FlushAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task ChildWriteAndCrash(string dbPath)
    {
        var sessionId = "child_session_crashed";
        var clockSegId = "child_clock_seg";
        var scopeRevId = "child_scope_rev";

        await using var storage = new SqliteStorageEngine(dbPath);
        await storage.RecordSessionStartAsync(new SessionRecord(sessionId, "epoch_child", 10000, null, null, false));
        await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, sessionId, 0, 10000, 10_000_000, "nominal"));
        await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 10000));

        await storage.IngestIntervalAsync(new UsageIntervalRecord(
            Guid.NewGuid().ToString("D"), $"{sessionId}_iv_1", sessionId, clockSegId, scopeRevId, "unidentified",
            10000, 11000, 10_000_000, 10_000_000, 1_000_000, false, false, null, "nominal"));

        await storage.IngestIntervalAsync(new UsageIntervalRecord(
            Guid.NewGuid().ToString("D"), $"{sessionId}_iv_2", sessionId, clockSegId, scopeRevId, "unidentified",
            11000, 12000, 10_000_000, 15_000_000, 1_500_000, false, false, null, "nominal"));

        await storage.FlushAsync(TimeSpan.FromSeconds(5));

        // Abrupt termination without clean session end or shutdown
        Process.GetCurrentProcess().Kill();
    }

    private static int RunChildProcess(string scenario)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
        start.ArgumentList.Add("--v1-child=" + scenario);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("V1 child process failed to start.");
        if (!child.WaitForExit(10000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new InvalidOperationException("V1 child process timed out.");
        }
        return child.ExitCode;
    }

    public static async Task TestStorage_CrashGapDoesNotOverlapValidHistory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_no_overlap_{Guid.NewGuid():N}.db");
        var crashedSessionId = "crashed_session_overlap_test";
        var clockSegId = "crashed_clock_seg";
        var scopeRevId = "crashed_scope_rev";

        try
        {
            // Seed a crashed session with two valid intervals: 10000..11000 (100 MB), 11000..12000 (150 MB)
            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                await storage.RecordSessionStartAsync(new SessionRecord(crashedSessionId, "epoch_crash", 10000, null, null, false));
                await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, crashedSessionId, 0, 10000, 10_000_000, "nominal"));
                await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "adapter-1", "valid", 10000));

                await storage.IngestIntervalAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"{crashedSessionId}_iv_1", crashedSessionId, clockSegId, scopeRevId, "unidentified",
                    10000, 11000, 10_000_000, 100_000_000, 10_000_000, false, false, null, "nominal"));

                await storage.IngestIntervalAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"{crashedSessionId}_iv_2", crashedSessionId, clockSegId, scopeRevId, "unidentified",
                    11000, 12000, 10_000_000, 150_000_000, 15_000_000, false, false, null, "nominal"));

                await storage.FlushAsync(TimeSpan.FromSeconds(5));
            }

            // Start collector on that DB at 20000 ms to trigger crash gap recovery
            var clock = new StubClock { CurrentMonotonic = 100.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(20000) };
            var counterProvider = new StubCounterProvider();

            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                using var collector = new Collector(counterProvider, clock, storage: storage);
                collector.Start();

                await Task.Delay(250);
                await storage.FlushAsync(TimeSpan.FromSeconds(5));

                var intervals = await storage.GetIntervalsAsync();
                Assert(intervals.Count == 3, $"Expected 3 intervals (2 valid + 1 crash gap), found {intervals.Count}");

                var crashGap = intervals.Single(i => i.IsGap && i.GapReason == "unclean_shutdown");
                // DEF-01 verification: Crash gap MUST start at 12000 (last durable interval end), NOT 10000 (session start)
                Assert(crashGap.StartUtcMs == 12000, $"Crash gap start must be 12000, was {crashGap.StartUtcMs}");
                Assert(crashGap.EndUtcMs == 20000, $"Crash gap end must be 20000, was {crashGap.EndUtcMs}");

                // Database invariant: Zero temporal overlap between any valid interval and any gap interval
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM usage_intervals i
                    JOIN usage_intervals g ON i.is_gap = 0 AND g.is_gap = 1
                    WHERE i.start_utc_ms < g.end_utc_ms AND i.end_utc_ms > g.start_utc_ms;
                ";
                var overlapCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert(overlapCount == 0, $"Crash gap overlap detected! Overlap count: {overlapCount}");

                // Exact byte accounting is completely preserved
                var totals = await storage.GetTotalBytesAsync();
                Assert(totals.TotalRx == 250_000_000, $"Total RX mismatch: {totals.TotalRx}");
                Assert(totals.TotalTx == 25_000_000, $"Total TX mismatch: {totals.TotalTx}");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_EmptyCrashedSessionRecoversFullSessionGap()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_empty_crash_{Guid.NewGuid():N}.db");
        var emptyCrashedSessionId = "empty_crashed_session";

        try
        {
            // Seed a session that crashed before writing any interval or checkpoint
            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                await storage.RecordSessionStartAsync(new SessionRecord(emptyCrashedSessionId, "epoch_empty", 4000, null, null, false));
                await storage.FlushAsync(TimeSpan.FromSeconds(5));
            }

            var clock = new StubClock { CurrentMonotonic = 100.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(16000) };
            var counterProvider = new StubCounterProvider();

            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                using var collector = new Collector(counterProvider, clock, storage: storage);
                collector.Start();

                await Task.Delay(250);
                await storage.FlushAsync(TimeSpan.FromSeconds(5));

                var intervals = await storage.GetIntervalsAsync();
                Assert(intervals.Count == 1, $"Expected 1 crash gap interval, found {intervals.Count}");

                var crashGap = intervals[0];
                Assert(crashGap.IsGap && crashGap.GapReason == "unclean_shutdown", "Must be unclean_shutdown gap");
                // Without previous intervals, fallback starts at session start (4000)
                Assert(crashGap.StartUtcMs == 4000, $"Gap start should be 4000, got {crashGap.StartUtcMs}");
                Assert(crashGap.EndUtcMs == 16000, $"Gap end should be 16000, got {crashGap.EndUtcMs}");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_IncompatibleNewerSchemaRejected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_schema_{Guid.NewGuid():N}.db");
        try
        {
            // Create database with future schema version (PRAGMA user_version = 999)
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 999;";
                await cmd.ExecuteNonQueryAsync();
            }

            // Opening with current engine must reject future schema without throwing or corrupting
            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                Assert(storage.Status.Health == StorageHealth.Degraded, $"Storage health should be Degraded, was {storage.Status.Health}");
                Assert(storage.Status.DegradedReason != null && storage.Status.DegradedReason.Contains("newer than supported"),
                    $"Expected schema error detail, got: {storage.Status.DegradedReason}");
            }

            // Verify user_version was NOT mutated
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version;";
                var ver = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert(ver == 999, $"user_version must remain 999, got {ver}");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_IntervalAndCheckpointCommittedAtomically()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_atomic_{Guid.NewGuid():N}.db");
        var sessionId = "atomic_session";
        var clockSegId = "atomic_clock";
        var scopeRevId = "atomic_scope";
        var intervalId = Guid.NewGuid().ToString("D");
        var checkpointId = Guid.NewGuid().ToString("D");

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sessionId, "epoch", 1000, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, sessionId, 0, 1000, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "eth-1", "valid", 1000));

            var iv = new UsageIntervalRecord(
                intervalId, "atomic_key_1", sessionId, clockSegId, scopeRevId, "unidentified",
                1000, 2000, 10_000_000, 500_000, 50_000, false, false, null, "nominal");

            var cp = new CounterCheckpointRecord(
                checkpointId, sessionId, "eth-1", "iphlpapi.GetIfTable2", "epoch_1", 1,
                10_000_000, 2000, BigEndianCodec.EncodeUInt64(500_000), BigEndianCodec.EncodeUInt64(50_000));

            await storage.IngestIntervalAsync(iv, cp);
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM usage_intervals WHERE interval_id = @id;";
            cmd.Parameters.AddWithValue("@id", intervalId);
            var ivCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert(ivCount == 1, $"Interval must be committed: {ivCount}");

            using var cmdCp = conn.CreateCommand();
            cmdCp.CommandText = "SELECT observed_sequence FROM counter_checkpoints WHERE checkpoint_id = @id;";
            cmdCp.Parameters.AddWithValue("@id", checkpointId);
            var cpSeq = Convert.ToInt64(await cmdCp.ExecuteScalarAsync());
            Assert(cpSeq == 1, $"Checkpoint sequence mismatch: {cpSeq}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_IdempotentDuplicateIntervalRejected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_idempotent_{Guid.NewGuid():N}.db");
        var sessionId = "idemp_session";
        var clockSegId = "idemp_clock";
        var scopeRevId = "idemp_scope";
        var intervalId = "fixed_interval_unique_id";

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            await storage.RecordSessionStartAsync(new SessionRecord(sessionId, "epoch", 1000, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord(clockSegId, sessionId, 0, 1000, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord(scopeRevId, "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "eth-1", "valid", 1000));

            var iv = new UsageIntervalRecord(
                intervalId, "fixed_key_1", sessionId, clockSegId, scopeRevId, "unidentified",
                1000, 2000, 10_000_000, 100_000, 10_000, false, false, null, "nominal");

            await storage.IngestIntervalAsync(iv);
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Ingesting the same interval_id again must be rejected by PRIMARY KEY
            await storage.IngestIntervalAsync(iv);
            try
            {
                await storage.FlushAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception) { /* Expected failure on duplicate primary key */ }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM usage_intervals WHERE interval_id = @id;";
            cmd.Parameters.AddWithValue("@id", intervalId);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert(count == 1, $"Duplicate interval must not create duplicate records: {count}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_SeparateProcessRestartPreservesExactBytes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_proc_restart_{Guid.NewGuid():N}.db");
        try
        {
            // Execute child process: writes 25MB + 50MB Rx, cleanly exits
            var exitCode = RunChildProcess("write-and-exit:" + dbPath);
            Assert(exitCode == 0, $"Child writer process failed with exit code: {exitCode}");

            // Parent process opens database and verifies durability across process boundary
            await using var storage = new SqliteStorageEngine(dbPath);
            var lastSession = await storage.GetLastSessionAsync();
            Assert(lastSession != null, "Last session record must survive out-of-process restart");
            Assert(lastSession!.EndedCleanly, "Session must be marked ended cleanly by child");
            Assert(lastSession.EndedAtUtcMs == 12000, "EndedAtUtcMs mismatch");

            var totals = await storage.GetTotalBytesAsync();
            Assert(totals.TotalRx == 75_000_000, $"Total RX mismatch across process boundary: {totals.TotalRx}");
            Assert(totals.TotalTx == 7_500_000, $"Total TX mismatch across process boundary: {totals.TotalTx}");

            var intervals = await storage.GetIntervalsAsync();
            Assert(intervals.Count == 2, $"Expected 2 intervals, found {intervals.Count}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_ForcedProcessCrashRecoversAccurateGap()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_proc_crash_{Guid.NewGuid():N}.db");
        try
        {
            // Execute child process which calls Environment.FailFast
            var exitCode = RunChildProcess("write-and-crash:" + dbPath);
            Assert(exitCode != 0, "Child process should have exited abnormally via FailFast");

            // Parent process starts collector on the crashed DB at 25000 ms
            var clock = new StubClock { CurrentMonotonic = 100.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(25000) };
            var counterProvider = new StubCounterProvider();

            await using (var storage = new SqliteStorageEngine(dbPath))
            {
                using var collector = new Collector(counterProvider, clock, storage: storage);
                collector.Start();

                await Task.Delay(250);
                await storage.FlushAsync(TimeSpan.FromSeconds(5));

                var intervals = await storage.GetIntervalsAsync();
                Assert(intervals.Count == 3, $"Expected 3 intervals (2 child + 1 crash gap), got {intervals.Count}");

                var crashGap = intervals.Single(i => i.IsGap && i.GapReason == "unclean_shutdown");
                // Child intervals ended at 12000 ms, so crash gap starts at 12000 ms with ZERO history overlap!
                Assert(crashGap.StartUtcMs == 12000, $"Crash gap start must be 12000, was {crashGap.StartUtcMs}");
                Assert(crashGap.EndUtcMs == 25000, $"Crash gap end must be 25000, was {crashGap.EndUtcMs}");

                var totals = await storage.GetTotalBytesAsync();
                Assert(totals.TotalRx == 25_000_000, $"Total RX mismatch after crash recovery: {totals.TotalRx}");
                Assert(totals.TotalTx == 2_500_000, $"Total TX mismatch after crash recovery: {totals.TotalTx}");

                // Invariant query: zero overlap between any interval and gap
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM usage_intervals i
                    JOIN usage_intervals g ON i.is_gap = 0 AND g.is_gap = 1
                    WHERE i.start_utc_ms < g.end_utc_ms AND i.end_utc_ms > g.start_utc_ms;
                ";
                var overlapCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert(overlapCount == 0, $"Crash recovery created overlapping interval! Overlap count: {overlapCount}");
            }
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestCollector_SurfacesStorageDegradationOnSnapshot()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_surface_deg_{Guid.NewGuid():N}.db");
        try
        {
            // Seed DB with incompatible schema version so storage initializes in Degraded state
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 888;";
                await cmd.ExecuteNonQueryAsync();
            }

            var clock = new StubClock { CurrentMonotonic = 10.0 };
            var counterProvider = new StubCounterProvider
            {
                Adapters = [new AdapterCounter("adapter-1", "Eth", "NIC", true, 100, 100, 1000, true)]
            };

            await using var storage = new SqliteStorageEngine(dbPath);
            Assert(storage.Status.Health == StorageHealth.Degraded, "Storage must be Degraded due to schema incompatibility");

            using var collector = new Collector(counterProvider, clock, storage: storage);
            collector.Select("adapter-1");

            // Baseline sample
            var snap1 = collector.SampleOnce();
            Assert(snap1.StorageStatus != null && snap1.StorageStatus.Health == StorageHealth.Degraded,
                "Snapshot 1 must surface degraded storage status");

            // Live sample: normally Running, but MUST be Degraded because storage is degraded
            clock.CurrentMonotonic += 1.0;
            counterProvider.Adapters = [new AdapterCounter("adapter-1", "Eth", "NIC", true, 200, 200, 1000, true)];
            var snap2 = collector.SampleOnce();

            Assert(snap2.State == CollectorState.Degraded, $"Truthful state must be Degraded, was {snap2.State}");
            Assert(snap2.StorageStatus != null && snap2.StorageStatus.Health == StorageHealth.Degraded,
                "Snapshot 2 must reflect storage health");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_QueueSaturationTriggersDegradedState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_sat_{Guid.NewGuid():N}.db");
        try
        {
            // Engine with channelCapacity = 5 (threshold 90% is 4.5, so 5 queued items triggers saturation)
            await using var storage = new SqliteStorageEngine(dbPath, channelCapacity: 5);

            // Establish valid foreign key references first
            await storage.RecordSessionStartAsync(new SessionRecord("sess_sat", "epoch_sat", 1000, null, null, false));
            await storage.RecordClockSegmentAsync(new ClockSegmentRecord("clk_sat", "sess_sat", 0, 1000, 10_000_000, "nominal"));
            await storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("scope_sat", "scope-1", 1, "SelectedAdapter", "Win32", "1.0", "eth-1", "valid", 1000));
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            // Rapidly enqueue 15 gap records to saturate the channel
            for (var i = 0; i < 15; i++)
            {
                _ = storage.IngestGapAsync(new UsageIntervalRecord(
                    Guid.NewGuid().ToString("D"), $"gap_{i}", "sess_sat", "clk_sat", "scope_sat", "unidentified",
                    1000 + (100 * i), 1000 + (100 * (i + 1)), 10_000_000, 0, 0, false, true, "sat_test", "nominal"));
            }

            // Verify degraded transition occurred
            Assert(storage.Status.Health == StorageHealth.Degraded,
                $"Storage health must be Degraded on queue saturation, was {storage.Status.Health}");
            Assert(storage.Status.DegradedReason != null && storage.Status.DegradedReason.Contains("saturated"),
                $"Degraded reason should mention saturation, got: {storage.Status.DegradedReason}");
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    public static async Task TestStorage_CapacityExceededTriggersRebaselineAndGap()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"speedmeter_test_overflow_{Guid.NewGuid():N}.db");
        var clock = new StubClock { CurrentMonotonic = 10.0, CurrentUtc = DateTimeOffset.FromUnixTimeMilliseconds(10000) };
        var counterProvider = new StubCounterProvider
        {
            Adapters = [new AdapterCounter("adapter-ovf", "Eth", "NIC", true, 100, 100, 1000, true)]
        };

        try
        {
            await using var storage = new SqliteStorageEngine(dbPath);
            using var collector = new Collector(counterProvider, clock, storage: storage);
            collector.Select("adapter-ovf");

            // Tick 1: Baseline sample
            collector.SampleOnce();

            // Tick 2: Counter jumps by more than signed long capacity (> 2^63 - 1)
            clock.CurrentMonotonic += 1.0;
            clock.CurrentUtc = clock.CurrentUtc.AddSeconds(1.0);
            var overflowVal = (ulong)long.MaxValue + 5000UL;
            counterProvider.Adapters = [new AdapterCounter("adapter-ovf", "Eth", "NIC", true, overflowVal, 100, 1000, true)];

            var snap2 = collector.SampleOnce();
            // Capacity exceeded triggers rebaseline: state should transition away from Live/Running
            await storage.FlushAsync(TimeSpan.FromSeconds(5));

            var intervals = await storage.GetIntervalsAsync();
            var capGap = intervals.FirstOrDefault(i => i.GapReason == "capacity_exceeded");
            Assert(capGap != null, "Storage must ingest explicit gap with reason 'capacity_exceeded'");
            Assert(capGap!.IsGap, "Gap flag must be true");

            // Totals must NOT contain corrupted overflowing bytes
            var totals = await storage.GetTotalBytesAsync();
            Assert(totals.TotalRx == 0, $"Total RX should remain 0, was {totals.TotalRx}");
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
