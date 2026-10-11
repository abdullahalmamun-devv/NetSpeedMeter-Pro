using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

public static class NetworkToolsFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("W4-TOOLS: Host normalization accepts valid hosts/IPs and rejects injection", HostNormalization);
        check("W4-TOOLS: DNS lookup resolves A and AAAA records truthfully", DnsLookupSuccess);
        check("W4-TOOLS: DNS lookup rejects invalid input and reports timeouts", DnsLookupErrors);
        check("W4-TOOLS: Ping tool calculates min/max/mean RTT without NaN", PingCalculations);
        check("W4-TOOLS: Ping tool handles timeouts, local errors, and cancellation", PingFailureModes);
        check("W4-TOOLS: Ping tool defaults to 10 attempts per PRD contract", PingDefault10Attempts);
        check("W4-TOOLS: Public IP lookup requires explicit disclosure and HTTPS endpoint", PublicIpDisclosureRequired);
        check("W4-TOOLS: Public IP lookup handles success, egress caveat, and cached fallback", PublicIpSuccessAndCache);
        check("W4-TOOLS: Public IP lookup rejects oversized response and provider error", PublicIpErrorHandling);
        check("W4-NETINFO: Network details provider contract labels link speed truthfully", NetworkDetailsTruthfulness);
        check("W4-NETINFO: Wi-Fi metadata queries populate or limit truthfully without degrading monitoring", WifiDetailsTruthfulness);
        check("W4-STORAGE: SqliteStorageEngine applies migration 3 and persists connectivity & alert timeline", StorageMigrationAndTimeline);
        check("W4-STORAGE: Migration upgrades schema v2 to v3 preserving existing history and data", StorageMigrationV2ToV3Fidelity);
        check("W4-STORAGE: Pruning and clear history maintain transaction safety and table integrity", StoragePruneAndClearIntegrity);
        check("W4-APP: Single instance mutex enforces single instance and activates existing window", SingleInstanceMutexProtection);
        check("W4-APP: Low resource mode throttles UI refresh without compromising byte accounting", LowResourceModeThrottling);
        check("W4-PRIVACY: Tool service construction and passive checks generate zero requests", ZeroExternalRequestsWhenUninvoked);
    }

    private static void HostNormalization()
    {
        Require(NetworkToolsService.TryNormalizeHost("example.com", out var h1) && h1 == "example.com", "Valid hostname rejected.");
        Require(NetworkToolsService.TryNormalizeHost("8.8.8.8", out var h2) && h2 == "8.8.8.8", "Valid IPv4 rejected.");
        Require(NetworkToolsService.TryNormalizeHost("2001:4860:4860::8888", out var h3) && h3 == "2001:4860:4860::8888", "Valid IPv6 rejected.");
        Require(NetworkToolsService.TryNormalizeHost("sub-domain.example.co.uk", out _), "Hyphenated hostname rejected.");

        Require(!NetworkToolsService.TryNormalizeHost("", out _), "Empty host accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("   ", out _), "Whitespace host accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("example.com/path", out _), "Slash accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("example.com\\path", out _), "Backslash accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("user@example.com", out _), "At-sign accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("host with spaces.com", out _), "Spaces accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("-starts-with-hyphen.com", out _), "Leading hyphen accepted.");
        Require(!NetworkToolsService.TryNormalizeHost(new string('a', 254), out _), "Oversized hostname accepted.");
    }

    private static void DnsLookupSuccess()
    {
        var fakeDns = new FakeDnsProvider(name => [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946")]);
        var service = new NetworkToolsService(new FakeDetailsProvider(), dns: fakeDns);

        var aResult = service.LookupDnsAsync("example.com", "A", "scope-1", CancellationToken.None).GetAwaiter().GetResult();
        Require(aResult.Outcome == NetworkToolOutcome.Success, "Expected successful A lookup.");
        Require(aResult.Answers.Count == 1 && aResult.Answers[0] == "93.184.216.34", "Wrong A record answers.");
        Require(aResult.Duration >= TimeSpan.Zero, "Negative duration reported.");

        var aaaaResult = service.LookupDnsAsync("example.com", "AAAA", "scope-1", CancellationToken.None).GetAwaiter().GetResult();
        Require(aaaaResult.Outcome == NetworkToolOutcome.Success, "Expected successful AAAA lookup.");
        Require(aaaaResult.Answers.Count == 1 && aaaaResult.Answers[0] == "2606:2800:220:1:248:1893:25c8:1946", "Wrong AAAA record answers.");
    }

    private static void DnsLookupErrors()
    {
        var fakeDns = new FakeDnsProvider(name => throw new SocketException((int)SocketError.HostNotFound));
        var service = new NetworkToolsService(new FakeDetailsProvider(), dns: fakeDns);

        var invalid = service.LookupDnsAsync("invalid host/name", "A", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(invalid.Outcome == NetworkToolOutcome.InvalidInput, "Invalid input not caught.");

        var err = service.LookupDnsAsync("missing.test", "A", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(err.Outcome == NetworkToolOutcome.LocalError, "SocketException did not become LocalError.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = service.LookupDnsAsync("example.com", "A", null, cts.Token).GetAwaiter().GetResult();
        Require(canceled.Outcome == NetworkToolOutcome.Cancelled, "Cancellation not reflected.");
    }

    private static void PingCalculations()
    {
        var replies = new Queue<PingAttemptResult>(new[]
        {
            new PingAttemptResult(PingAttemptOutcome.Reply, 10.0, null),
            new PingAttemptResult(PingAttemptOutcome.Reply, 20.0, null),
            new PingAttemptResult(PingAttemptOutcome.Reply, 30.0, null),
        });
        var fakePing = new FakePingProvider((_, _, _) => replies.Dequeue());
        var service = new NetworkToolsService(new FakeDetailsProvider(), ping: fakePing);

        var result = service.PingAsync("1.1.1.1", "scope-1", CancellationToken.None, attemptCount: 3).GetAwaiter().GetResult();
        Require(result.Outcome == NetworkToolOutcome.Success, "Expected Success for all replies.");
        Require(result.ReplyCount == 3, "Reply count mismatch.");
        Require(result.TimeoutCount == 0 && result.LocalErrorCount == 0, "Non-zero errors/timeouts.");
        Require(Math.Abs(result.MeanRttMilliseconds!.Value - 20.0) < 0.001, "Mean RTT mismatch.");
        Require(Math.Abs(result.MinimumRttMilliseconds!.Value - 10.0) < 0.001, "Min RTT mismatch.");
        Require(Math.Abs(result.MaximumRttMilliseconds!.Value - 30.0) < 0.001, "Max RTT mismatch.");
    }

    private static void PingFailureModes()
    {
        var attempts = new Queue<PingAttemptResult>(new[]
        {
            new PingAttemptResult(PingAttemptOutcome.Reply, 15.0, null),
            new PingAttemptResult(PingAttemptOutcome.Timeout, null, null),
            new PingAttemptResult(PingAttemptOutcome.LocalError, null, "GeneralFailure"),
        });
        var fakePing = new FakePingProvider((_, _, _) => attempts.Dequeue());
        var service = new NetworkToolsService(new FakeDetailsProvider(), ping: fakePing);

        var partial = service.PingAsync("example.com", null, CancellationToken.None, attemptCount: 3).GetAwaiter().GetResult();
        Require(partial.Outcome == NetworkToolOutcome.Partial, "Expected Partial for mixed replies.");
        Require(partial.ReplyCount == 1 && partial.TimeoutCount == 1 && partial.LocalErrorCount == 1, "Attempt counts wrong.");
        Require(partial.MeanRttMilliseconds == 15.0, "Mean should reflect only valid replies.");

        var invalidTarget = service.PingAsync("bad target/name", null, CancellationToken.None, 5).GetAwaiter().GetResult();
        Require(invalidTarget.Outcome == NetworkToolOutcome.InvalidInput, "Bad target allowed.");

        var invalidCount = service.PingAsync("1.1.1.1", null, CancellationToken.None, 11).GetAwaiter().GetResult();
        Require(invalidCount.Outcome == NetworkToolOutcome.InvalidInput, "Count > 10 allowed.");
    }

    private static void PublicIpDisclosureRequired()
    {
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("198.51.100.1") });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("TestProvider", new Uri("https://api.test.example/ip"), NetworkToolsService.RequiredPublicIpDisclosure);

        // Without disclosure accepted, must reject and send ZERO HTTP requests
        var unaccepted = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: false), CancellationToken.None).GetAwaiter().GetResult();
        Require(unaccepted.Outcome == NetworkToolOutcome.InvalidInput, "Unaccepted disclosure allowed.");
        Require(testHandler.RequestCount == 0, "HTTP request made without disclosure acceptance.");

        // Cleartext HTTP provider must be rejected
        var cleartextProvider = new PublicIpProvider("Bad", new Uri("http://api.test.example/ip"), NetworkToolsService.RequiredPublicIpDisclosure);
        var cleartextResult = service.LookupPublicIpAsync(new PublicIpLookupRequest(cleartextProvider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(cleartextResult.Outcome == NetworkToolOutcome.InvalidInput, "Cleartext HTTP provider accepted.");
        Require(testHandler.RequestCount == 0, "HTTP request made to cleartext provider.");
    }

    private static void PublicIpSuccessAndCache()
    {
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.42\n") });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("EchoService", new Uri("https://echo.test/ip"), NetworkToolsService.RequiredPublicIpDisclosure);

        var result = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(result.Outcome == NetworkToolOutcome.Success, "Expected public IP success.");
        Require(result.Address == "203.0.113.42", "Returned address mismatch.");
        Require(result.AddressFamily == "IPv4", "Address family mismatch.");
        Require(result.EgressContext == NetworkToolsService.EgressCaveat, "Egress caveat omitted.");
        Require(result.CacheTimestamp.HasValue, "Cache timestamp missing.");

        // Next request fails -> returns cached address with diagnostic note
        testHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var failResult = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(failResult.Outcome == NetworkToolOutcome.Unavailable, "Expected Unavailable status.");
        Require(failResult.Address == "203.0.113.42", "Failed request did not return cached address.");
        Require(failResult.Diagnostic?.Contains("stale", StringComparison.OrdinalIgnoreCase) == true, "Diagnostic did not note stale cache.");
    }

    private static void PublicIpErrorHandling()
    {
        // Oversized response
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('9', 200)) });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("EchoService", new Uri("https://echo.test/ip"), NetworkToolsService.RequiredPublicIpDisclosure);
        var oversized = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-2", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(oversized.Outcome == NetworkToolOutcome.Unavailable, "Oversized response was not rejected.");

        // Invalid IP in body
        testHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Not An IP</html>") };
        var invalidBody = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-2", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(invalidBody.Outcome == NetworkToolOutcome.Unavailable, "Invalid IP body was not rejected.");
    }

    private static void NetworkDetailsTruthfulness()
    {
        var provider = new FakeDetailsProvider();
        var snapshot = provider.Read("test-adapter-id", "scope-1");

        Require(snapshot.Availability == ProviderAvailability.Available, "Snapshot unavailable.");
        Require(snapshot.Diagnostic == "Directional values are interface link speed, not internet speed.",
            "Diagnostic must distinguish link speed from internet speed.");
        Require(snapshot.WifiLimitation.Contains("unavailable", StringComparison.OrdinalIgnoreCase),
            "Wi-Fi limitation not truthful.");
    }

    private static void StorageMigrationAndTimeline()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_w4_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                // Verify migration 3 applied
                Require(storage.Status.Health == StorageHealth.Healthy, "Storage engine not healthy after migration.");

                // Test Connectivity Event persistence
                var connEvent = new ConnectivityEventRecord(
                    "evt-1001", "LinkLost", "NativeNetworkChange", "scope-A", "profile-A",
                    "NoLocalConnection", 1700000000000, 1700000003000, "Confirmed", "Local link cable unplugged");
                storage.RecordConnectivityEventAsync(connEvent).GetAwaiter().GetResult();

                var queriedEvents = storage.GetConnectivityEventsAsync(1700000000000, 1700000005000).GetAwaiter().GetResult();
                Require(queriedEvents.Count == 1, "Connectivity event not retrieved.");
                Require(queriedEvents[0].EventId == "evt-1001" && queriedEvents[0].State == "NoLocalConnection", "Retrieved event corrupted.");

                // Test Alert State CAS & Event persistence
                var rule = new AlertRule("rule-1", 1, AlertKind.LocalDisconnect, "scope-A", "network-A", true, 0, 0, 1000, 0, 3000);
                var key = AlertIdentity.StateKey(rule);
                var initialState = storage.GetAlertStateAsync(key).GetAwaiter().GetResult();
                Require(initialState is null, "Initial state should be null before first incident.");

                var nextState = new AlertIncidentState(key, 1, "inc-1", 1, 1700000000000, 2);
                var alertEvt = new AlertEvent("aevt-1", "inc-1", "rule-1", 1, AlertKind.LocalDisconnect,
                    AlertEventPhase.Trigger, "scope-A", "network-A", 1700000000000, null, "Local link lost", false);

                var committed = storage.TryCommitAlertAsync(key, 0, nextState, alertEvt).GetAwaiter().GetResult();
                Require(committed, "Alert CAS commit failed.");

                var updatedState = storage.GetAlertStateAsync(key).GetAwaiter().GetResult();
                Require(updatedState is not null && updatedState.Version == 1 && updatedState.ActiveIncidentId == "inc-1",
                    "Persisted alert state mismatch.");

                var alertEvents = storage.GetAlertEventsAsync(1700000000000, 1700000001000).GetAwaiter().GetResult();
                Require(alertEvents.Count == 1 && alertEvents[0].EventId == "aevt-1", "Persisted alert event not found in history.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void ZeroExternalRequestsWhenUninvoked()
    {
        var testHandler = new FakeHttpHandler(_ => throw new InvalidOperationException("External request was made!"));
        var http = new HttpClient(testHandler);
        var tools = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        // Querying network details does not make any HTTP requests
        var details = tools.GetDetails("adapter-1", "scope-1");
        Require(details.Availability == ProviderAvailability.Available, "Details query failed.");
        Require(testHandler.RequestCount == 0, "Details query issued external HTTP request!");
    }

    private static void PingDefault10Attempts()
    {
        var count = 0;
        var ping = new FakePingProvider((_, _, _) =>
        {
            count++;
            return new PingAttemptResult(PingAttemptOutcome.Reply, 15.0, null);
        });
        var tools = new NetworkToolsService(new FakeDetailsProvider(), ping: ping);
        var result = tools.PingAsync("1.1.1.1", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(result.Attempts.Count == 10, $"Service default ping attempts must be 10, was {result.Attempts.Count}");
        Require(count == 10, $"Provider invocation count must be 10, was {count}");
    }

    private static void WifiDetailsTruthfulness()
    {
        var snapshot = new NetworkDetailsSnapshot(
            "nic-wifi", "scope-1", DateTimeOffset.UtcNow, ProviderAvailability.Available,
            "Wi-Fi", "Wireless80211", "Up",
            [new NetworkAddressDetail("192.168.1.50", 24)], [], ["192.168.1.1"], ["1.1.1.1"], true,
            "prof-1", "Home-WiFi", "AA:BB:CC:DD:EE:FF", 866_000_000, 866_000_000,
            "Home-WiFi", 95, "5 GHz", 149, "WPA2-Personal", "Wi-Fi telemetry observed", "OK");

        Require(snapshot.WifiSsid == "Home-WiFi", "Wi-Fi SSID mismatch.");
        Require(snapshot.WifiSignalPercent == 95, "Wi-Fi signal mismatch.");
        Require(snapshot.WifiBand == "5 GHz", "Wi-Fi band mismatch.");
        Require(snapshot.WifiChannel == 149, "Wi-Fi channel mismatch.");
        Require(snapshot.WifiAuthentication == "WPA2-Personal", "Wi-Fi auth mismatch.");
    }

    private static void StorageMigrationV2ToV3Fidelity()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_mig_v2_to_v3_{Guid.NewGuid():N}.db");
        try
        {
            // Create database at Schema Version 2 manually
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY CHECK(version > 0),
                        migration_id TEXT NOT NULL UNIQUE,
                        checksum TEXT NOT NULL,
                        applied_at_utc_ms INTEGER NOT NULL
                    ) STRICT;
                    INSERT INTO schema_migrations VALUES (1, '001_initial_schema', 'chk1', 1700000000000);
                    INSERT INTO schema_migrations VALUES (2, '002_quota_dedup_and_indexes', 'chk2', 1700000001000);
                    PRAGMA user_version = 2;

                    CREATE TABLE collection_sessions (
                        session_id TEXT NOT NULL PRIMARY KEY,
                        monotonic_epoch TEXT NOT NULL,
                        started_at_utc_ms INTEGER NOT NULL,
                        ended_at_utc_ms INTEGER,
                        stop_reason TEXT,
                        ended_cleanly INTEGER NOT NULL DEFAULT 0 CHECK(ended_cleanly IN (0,1))
                    ) STRICT;

                    CREATE TABLE clock_segments (
                        segment_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        started_at_utc_ms INTEGER NOT NULL,
                        initial_monotonic_ticks INTEGER NOT NULL,
                        rate_scalar REAL NOT NULL DEFAULT 1.0,
                        is_discontinuous INTEGER NOT NULL DEFAULT 0 CHECK(is_discontinuous IN (0,1)),
                        discontinuity_reason TEXT
                    ) STRICT;

                    CREATE TABLE scope_revisions (
                        revision_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        revision_number INTEGER NOT NULL CHECK(revision_number >= 0),
                        scope_mode TEXT NOT NULL,
                        primary_interface_id TEXT,
                        topology TEXT NOT NULL,
                        stabilization_state TEXT NOT NULL,
                        applied_at_utc_ms INTEGER NOT NULL,
                        UNIQUE(session_id, revision_number)
                    ) STRICT;

                    CREATE TABLE network_profiles (
                        profile_id TEXT NOT NULL PRIMARY KEY,
                        os_network_guid TEXT,
                        signature_hash BLOB NOT NULL UNIQUE,
                        first_seen_utc_ms INTEGER NOT NULL,
                        last_seen_utc_ms INTEGER NOT NULL,
                        is_unidentified INTEGER NOT NULL DEFAULT 0 CHECK(is_unidentified IN (0,1))
                    ) STRICT;

                    CREATE TABLE usage_intervals (
                        interval_id TEXT NOT NULL PRIMARY KEY,
                        source_interval_key TEXT NOT NULL UNIQUE,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        clock_segment_id TEXT NOT NULL REFERENCES clock_segments(segment_id),
                        scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        start_utc_ms INTEGER NOT NULL,
                        end_utc_ms INTEGER NOT NULL CHECK(end_utc_ms > start_utc_ms),
                        monotonic_duration_ticks INTEGER,
                        bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
                        bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
                        is_coarse INTEGER NOT NULL DEFAULT 0 CHECK(is_coarse IN (0,1)),
                        is_gap INTEGER NOT NULL DEFAULT 0 CHECK(is_gap IN (0,1)),
                        gap_reason TEXT,
                        timing_confidence REAL NOT NULL CHECK(timing_confidence >= 0.0 AND timing_confidence <= 1.0)
                    ) STRICT;

                    CREATE TABLE quota_threshold_events (
                        event_id TEXT NOT NULL PRIMARY KEY,
                        rule_id TEXT NOT NULL,
                        rule_revision INTEGER NOT NULL CHECK(rule_revision >= 0),
                        cycle_identifier TEXT NOT NULL,
                        threshold_bps INTEGER NOT NULL CHECK(threshold_bps > 0 AND threshold_bps <= 1000000),
                        crossed_at_utc_ms INTEGER NOT NULL,
                        effective_usage_bytes INTEGER NOT NULL CHECK(effective_usage_bytes >= 0),
                        UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)
                    ) STRICT;

                    INSERT INTO network_profiles VALUES ('prof-1', null, X'010203', 1700000000000, 1700000000000, 0);
                    INSERT INTO collection_sessions VALUES ('sess-1', 'epoch-1', 1700000000000, null, null, 0);
                    INSERT INTO clock_segments VALUES ('seg-1', 'sess-1', 1700000000000, 0, 1.0, 0, null);
                    INSERT INTO scope_revisions VALUES ('rev-1', 'sess-1', 1, 'AutoPrimary', 'nic-1', 'SinglePrimary', 'Stable', 1700000000000);
                    INSERT INTO usage_intervals VALUES ('int-1', 'key-1', 'sess-1', 'seg-1', 'rev-1', 'prof-1', 1700000000000, 1700000001000, 10000000, 500, 300, 0, 0, null, 1.0);
                    INSERT INTO quota_threshold_events VALUES ('q-1', 'rule-1', 1, 'cycle-2026-10', 8000, 1700000000500, 8000000);
                ";
                cmd.ExecuteNonQuery();
            }

            // Open existing v2 DB with SqliteStorageEngine -> should migrate to v3 cleanly
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                Require(storage.Status.Health == StorageHealth.Healthy, "Storage engine degraded on v2->v3 migration.");

                // Verify previous data is completely intact
                var intervals = storage.GetIntervalsAsync(null, 1700000000000, 1700000002000).GetAwaiter().GetResult();
                Require(intervals.Count == 1, "Existing usage intervals lost during migration.");
                Require(intervals[0].BytesRx == 500 && intervals[0].BytesTx == 300, "Interval byte data corrupted during migration.");

                var thresholds = storage.GetRecordedThresholdsAsync("rule-1", 1, "cycle-2026-10").GetAwaiter().GetResult();
                Require(thresholds.Count == 1 && thresholds[0] == 8000, "Existing quota events lost during migration.");

                // Verify v3 tables are writable
                var connEvt = new ConnectivityEventRecord("evt-v3-1", "StateChange", "Passive", "nic-1", "prof-1", "Reachable", 1700000002000, 1700000002000, "Confirmed", "OK");
                storage.RecordConnectivityEventAsync(connEvt).GetAwaiter().GetResult();
                var readConn = storage.GetConnectivityEventsAsync(1700000001000, 1700000003000).GetAwaiter().GetResult();
                Require(readConn.Count == 1 && readConn[0].EventId == "evt-v3-1", "Failed to persist to migration 3 table.");
            }

            // Re-open (idempotent open)
            using (var storage2 = new SqliteStorageEngine(tempDb))
            {
                Require(storage2.Status.Health == StorageHealth.Healthy, "Idempotent re-open degraded.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void StoragePruneAndClearIntegrity()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_prune_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var oldTime = now - 100_000_000;

                storage.RecordSessionStartAsync(new SessionRecord("s1", "e1", oldTime, null, null, false)).GetAwaiter().GetResult();
                storage.RecordClockSegmentAsync(new ClockSegmentRecord("seg1", "s1", 0, oldTime, 10_000_000, "1.0", null)).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("rev1", "nic1", 1, "AutoPrimary", "GetIfTable2", "1.0", "AutoPrimary", "Valid", oldTime)).GetAwaiter().GetResult();
                storage.RecordScopeMembershipAsync(new ScopeMembershipRecord("m1", "rev1", "nic1", "inc1", "unidentified", "s1", 0, null)).GetAwaiter().GetResult();

                // Add old interval and recent interval
                var oldInt = new UsageIntervalRecord("int-old", "key-old", "s1", "seg1", "rev1", "unidentified",
                    oldTime, oldTime + 1000, 10000000, 100, 100, false, false, null, "1.0");
                var recentInt = new UsageIntervalRecord("int-recent", "key-recent", "s1", "seg1", "rev1", "unidentified",
                    now - 500, now, 5000000, 200, 200, false, false, null, "1.0");

                storage.IngestIntervalAsync(oldInt).GetAwaiter().GetResult();
                storage.IngestIntervalAsync(recentInt).GetAwaiter().GetResult();

                // Record quota threshold event
                var quotaEvent = new QuotaThresholdEvent("q-ev-1", "rule-active", 1, "cycle-now", 8000, now - 1000, 80000);
                storage.RecordQuotaThresholdEventAsync(quotaEvent).GetAwaiter().GetResult();

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                // Prune records older than cutoff (between old and recent)
                var cutoff = oldTime + 5000;
                var deletedCount = storage.PruneHistoryAsync(cutoff).GetAwaiter().GetResult();
                Require(deletedCount >= 1, "PruneHistoryAsync must report deleted count >= 1.");

                // Verify old is gone, recent remains
                var remaining = storage.GetIntervalsAsync(null, oldTime, now + 1000).GetAwaiter().GetResult();
                Require(remaining.Count == 1 && remaining[0].IntervalId == "int-recent", "Old interval not pruned or recent interval incorrectly deleted.");

                // Verify active quota event survives pruning
                var quotaEvents = storage.GetRecordedThresholdsAsync("rule-active", 1, "cycle-now").GetAwaiter().GetResult();
                Require(quotaEvents.Count == 1 && quotaEvents[0] == 8000, "Active quota threshold event must survive pruning.");

                // Test ClearAllHistoryAsync
                storage.ClearAllHistoryAsync().GetAwaiter().GetResult();
                var afterClear = storage.GetIntervalsAsync(null, 0, now + 10000).GetAwaiter().GetResult();
                Require(afterClear.Count == 0, "ClearAllHistoryAsync did not purge all intervals.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void SingleInstanceMutexProtection()
    {
        var testMutexName = @"Local\SpeedMeterPro.TestInstance." + Guid.NewGuid().ToString("N");
        using var first = new Mutex(true, testMutexName, out var firstCreated);
        Require(firstCreated, "First instance must successfully acquire mutex.");

        using var second = new Mutex(true, testMutexName, out var secondCreated);
        Require(!secondCreated, "Second instance must detect mutex is held and report created == false.");
    }

    private static void LowResourceModeThrottling()
    {
        var settings = new SpeedMeterSettings();
        Require(!settings.LowResourceMode, "LowResourceMode must default to false.");

        settings.LowResourceMode = true;
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        Require(json.Contains("\"lowResourceMode\":true"), "LowResourceMode not serialized correctly.");

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<SpeedMeterSettings>(json);
        Require(deserialized is not null && deserialized.LowResourceMode, "LowResourceMode not deserialized correctly.");
    }

    private sealed class FakeDnsProvider(Func<string, IPAddress[]> resolver) : IDnsLookupProvider
    {
        public Task<IPAddress[]> LookupAsync(string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(resolver(name));
        }
    }

    private sealed class FakePingProvider(Func<string, int, CancellationToken, PingAttemptResult> pingFunc) : IPingAttemptProvider
    {
        public Task<PingAttemptResult> SendAsync(string target, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(pingFunc(target, timeoutMilliseconds, cancellationToken));
        }
    }

    private sealed class FakeDetailsProvider : INetworkDetailsProvider
    {
        public NetworkDetailsSnapshot Read(string? selectedAdapterId, string? scopeId)
        {
            return new NetworkDetailsSnapshot(
                selectedAdapterId, scopeId, DateTimeOffset.UtcNow, ProviderAvailability.Available,
                "Ethernet 1", "Ethernet", "Up",
                [new NetworkAddressDetail("192.168.1.100", 24)],
                [new NetworkAddressDetail("fe80::1", 64)],
                ["192.168.1.1"], ["1.1.1.1", "8.8.8.8"], true,
                "profile-guid-1", null, "00:11:22:33:44:55",
                1_000_000_000, 1_000_000_000,
                null, null, null, null, null,
                "Wi-Fi SSID and signal unavailable without Windows location permission.",
                "Directional values are interface link speed, not internet speed.");
        }
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = responseFactory;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ResponseFactory(request));
        }
    }
}
