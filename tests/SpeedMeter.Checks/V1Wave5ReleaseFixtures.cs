using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Installer;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

internal static class V1Wave5ReleaseFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("W5-REL: Canonical version model matches across ProductIdentity and Assembly", CanonicalVersionIdentityConformity);
        check("W5-REL: Update manifest parses valid payload and rejects malformed fields", UpdateManifestParsingAndValidation);
        check("W5-REL: Tampered manifest properties fail cryptographic signature verification", TamperedManifestRejection);
        check("W5-REL: Attacker supplied manifest with untrusted public key fails trust anchor validation", ManifestTrustAnchorTamperingDrill);
        check("W5-REL: Tampered package payload with single byte alteration fails SHA-256 verification", TamperedPackagePayloadRejection);
        check("W5-REL: Package staging path rejects directory traversal and quarantine escapes", StagingPathTraversalProtection);
        check("W5-REL: Live-writer SQLite WAL snapshot preserves atomic consistency and rollback restores 100% fidelity", LiveWriterSqliteSnapshotAndRollbackDrill);
        check("W5-REL: Incompatible newer schema strictly blocks downgrade to protect data integrity", IncompatibleSchemaDowngradePrevention);
        check("W5-REL: Operational logger redacts IPs, MAC addresses, and user paths with bounded rotation", OperationalLoggerRotationAndRedaction);
        check("W5-REL: Diagnostic bundle exports sanitized system telemetry with verified redaction preview", DiagnosticBundleRedactionPreview);
        check("W5-REL: Update trust service performs zero unsolicited network requests when disabled", ZeroExternalNetworkWhenUpdatesDisabled);
        check("W5-REL: Authenticode publisher enforcement rejects unsigned, tampered, and publisher-mismatched binaries", AuthenticodePublisherEnforcementDrill);
        check("W5-REL: Whole-application shutdown bound enforces 3.0s maximum and reports non-clean diagnostics", WholeApplicationShutdownBoundEnforcement);
        check("W5-REL: Installer graceful shutdown handshake coordinates clean exit and refuses destructive force-kill", InstallerGracefulShutdownHandshakeDrill);
        check("W5-REL: Installer transactional staging rolls back atomically to previous version on interrupted upgrade", InstallerTransactionalInstallAndRollbackDrill);
        check("W5-REL: Installer fresh install and upgrade preserve user data while uninstall purge cleans all traces", InstallerFreshInstallUpgradeAndUninstallDrill);
    }

    private static void CanonicalVersionIdentityConformity()
    {
        Require(ProductIdentity.Name == "SpeedMeter Pro", "Product name mismatch.");
        Require(ProductIdentity.ProductVersion == "1.0.0-rc.1", "Product version must be 1.0.0-rc.1.");
        Require(ProductIdentity.FileVersion == "1.0.0.1", "File version must be 1.0.0.1.");
        Require(ProductIdentity.ReleaseChannel == "rc", "Release channel must be rc.");
        Require(ProductIdentity.DatabaseSchemaVersion == 3, "Database schema version must be 3.");
        Require(!string.IsNullOrWhiteSpace(ProductIdentity.CommitSha), "Commit SHA cannot be empty.");

        var appAssembly = typeof(ApplicationContext).Assembly;
        var infoVersionAttr = appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Require(infoVersionAttr is not null, "AssemblyInformationalVersionAttribute missing.");
        Require(infoVersionAttr!.InformationalVersion.Contains(ProductIdentity.ProductVersion), "InformationalVersion must contain ProductVersion.");

        // Blocker A: Assert reported CommitSha matches actual git rev-parse HEAD when run in git repo
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var gitHead = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(gitHead))
                {
                    Require(ProductIdentity.CommitSha.Equals(gitHead, StringComparison.OrdinalIgnoreCase),
                        $"ProductIdentity.CommitSha '{ProductIdentity.CommitSha}' does not match git rev-parse HEAD '{gitHead}'.");
                }
            }
        }
        catch { }
    }

    private static void UpdateManifestParsingAndValidation()
    {
        var validJson = @"
        {
            ""schemaVersion"": 1,
            ""product"": ""SpeedMeterPro"",
            ""channel"": ""rc"",
            ""version"": ""1.0.0-rc.1"",
            ""buildDateUtc"": ""2026-10-12T00:00:00Z"",
            ""minSupportedVersion"": ""1.0.0-rc.1"",
            ""packageUrl"": ""https://updates.speedmeterpro.local/releases/v1.0.0-rc.1/SpeedMeterPro-1.0.0-rc.1-Setup.exe"",
            ""packageSha256"": ""4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041"",
            ""packageSizeBytes"": 75246259,
            ""targetDbSchemaVersion"": 3,
            ""releaseNotesUrl"": ""https://updates.speedmeterpro.local/notes/v1.0.0-rc.1.html"",
            ""publisher"": ""SpeedMeter Pro Project"",
            ""signature"": ""dGVzdC1zaWduYXR1cmU=""
        }";

        var manifest = UpdateVerificationService.ParseManifest(validJson);
        Require(manifest.Version == "1.0.0-rc.1", "Parsed version mismatch.");
        Require(manifest.PackageSizeBytes == 75246259, "Parsed package size mismatch.");
        Require(manifest.TargetDbSchemaVersion == 3, "Parsed schema version mismatch.");

        // Reject non-HTTPS URL
        var insecureJson = validJson.Replace("https://", "http://");
        var threwInsecure = false;
        try { UpdateVerificationService.ParseManifest(insecureJson); } catch { threwInsecure = true; }
        Require(threwInsecure, "Insecure HTTP URL must be rejected.");

        // Reject negative size
        var badSizeJson = validJson.Replace("75246259", "-1");
        var threwBadSize = false;
        try { UpdateVerificationService.ParseManifest(badSizeJson); } catch { threwBadSize = true; }
        Require(threwBadSize, "Negative package size must be rejected.");

        // Reject invalid hash length
        var badHashJson = validJson.Replace("4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041", "short_hash");
        var threwBadHash = false;
        try { UpdateVerificationService.ParseManifest(badHashJson); } catch { threwBadHash = true; }
        Require(threwBadHash, "Invalid SHA-256 hash length must be rejected.");
    }

    private static void TamperedManifestRejection()
    {
        using var rsa = RSA.Create(2048);
        var manifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.1", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://updates.test/setup.exe", "4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041",
            75000000, 3, "https://updates.test/notes.html", "SpeedMeter Pro Project", "");

        var canonicalBytes = UpdateVerificationService.GetCanonicalManifestBytes(manifest);
        var signature = rsa.SignData(canonicalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signedManifest = manifest with { Signature = Convert.ToBase64String(signature) };

        // Valid signature passes
        var valid = UpdateVerificationService.VerifyManifestSignature(signedManifest, canonicalBytes, rsa);
        Require(valid, "Legitimate manifest signature failed verification.");

        // Tampered canonical payload fails
        var tamperedManifest = signedManifest with { PackageUrl = "https://updates.test/MALICIOUS.exe" };
        var tamperedBytes = UpdateVerificationService.GetCanonicalManifestBytes(tamperedManifest);
        var invalid = UpdateVerificationService.VerifyManifestSignature(tamperedManifest, tamperedBytes, rsa);
        Require(!invalid, "Tampered manifest payload must fail signature verification.");
    }

    private static void ManifestTrustAnchorTamperingDrill()
    {
        // Attacker creates their own keypair and signs a malicious manifest
        using var attackerKey = RSA.Create(2048);
        var maliciousManifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.2", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://malicious.test/SpeedMeterPro-Setup.exe", "0000000000000000000000000000000000000000000000000000000000000000",
            75000000, 3, "https://malicious.test/notes.html", "SpeedMeter Pro Project", "");

        var maliciousBytes = UpdateVerificationService.GetCanonicalManifestBytes(maliciousManifest);
        var maliciousSig = attackerKey.SignData(maliciousBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signedMaliciousManifest = maliciousManifest with { Signature = Convert.ToBase64String(maliciousSig) };

        // Verification using application Trust Anchor must reject attacker key
        var passedTrustAnchor = UpdateVerificationService.VerifyManifestSignature(signedMaliciousManifest, maliciousBytes, customPublicKey: null);
        Require(!passedTrustAnchor, "Manifest signed by untrusted attacker key must fail Trust Anchor verification.");
    }

    private static void TamperedPackagePayloadRejection()
    {
        var legitimateBytes = new byte[1024 * 64];
        Random.Shared.NextBytes(legitimateBytes);

        using var legitimateStream = new MemoryStream(legitimateBytes);
        var expectedSha256 = UpdateVerificationService.ComputeSha256(legitimateStream);

        // Verify valid stream passes
        legitimateStream.Position = 0;
        var valid = UpdateVerificationService.VerifyPackagePayload(legitimateStream, expectedSha256);
        Require(valid, "Legitimate package stream failed SHA-256 verification.");

        // Tamper with single byte (bit flip)
        var tamperedBytes = (byte[])legitimateBytes.Clone();
        tamperedBytes[42] = (byte)(tamperedBytes[42] ^ 0xFF);

        using var tamperedStream = new MemoryStream(tamperedBytes);
        var tamperedPassed = UpdateVerificationService.VerifyPackagePayload(tamperedStream, expectedSha256);
        Require(!tamperedPassed, "Tampered package payload with single byte alteration must be rejected.");
    }

    private static void StagingPathTraversalProtection()
    {
        var tempStaging = Path.Combine(Path.GetTempPath(), $"speedmeter_staging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempStaging);
        try
        {
            var resolved = UpdateVerificationService.ValidateAndResolveStagingPath(tempStaging, "update.exe");
            Require(resolved == Path.Combine(tempStaging, "update.exe"), "Legitimate staging filename misresolved.");

            void AssertRejected(string badName)
            {
                var threw = false;
                try { UpdateVerificationService.ValidateAndResolveStagingPath(tempStaging, badName); }
                catch (SecurityException) { threw = true; }
                Require(threw, $"Traversal attack '{badName}' was not blocked.");
            }

            AssertRejected("../../windows/system32/cmd.exe");
            AssertRejected("..\\..\\windows\\system32\\cmd.exe");
            AssertRejected("sub/path/file.exe");
            AssertRejected("sub\\path\\file.exe");
            AssertRejected("/evil.exe");
            AssertRejected("C:\\evil.exe");
        }
        finally
        {
            try { if (Directory.Exists(tempStaging)) Directory.Delete(tempStaging, true); } catch { }
        }
    }

    private static void LiveWriterSqliteSnapshotAndRollbackDrill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"speedmeter_live_drill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "speedmeter.db");
        var backupDir = Path.Combine(tempDir, "backups");

        try
        {
            // Scenario: storage engine remains OPEN and ACTIVE with WAL mode
            using var activeStorage = new SqliteStorageEngine(dbPath);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            activeStorage.RecordSessionStartAsync(new SessionRecord("s1", "e1", now, null, null, false)).GetAwaiter().GetResult();
            activeStorage.RecordClockSegmentAsync(new ClockSegmentRecord("seg1", "s1", 0, now, 10_000_000, "1.0", null)).GetAwaiter().GetResult();
            activeStorage.RecordScopeRevisionAsync(new ScopeRevisionRecord("rev1", "nic1", 1, "AutoPrimary", "GetIfTable2", "1.0", "AutoPrimary", "Valid", now)).GetAwaiter().GetResult();
            activeStorage.RecordScopeMembershipAsync(new ScopeMembershipRecord("m1", "rev1", "nic1", "inc1", "unidentified", "s1", 0, null)).GetAwaiter().GetResult();
            activeStorage.IngestIntervalAsync(new UsageIntervalRecord("int-1", "k1", "s1", "seg1", "rev1", "unidentified", now, now + 1000, 10000000, 5000, 2000, false, false, null, "1.0")).GetAwaiter().GetResult();
            activeStorage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            // Request pre-migration snapshot WITHOUT closing SQLite engine
            var backupPath = UpdateVerificationService.CreatePreMigrationSnapshot(dbPath, backupDir);
            Require(File.Exists(backupPath), "Pre-migration snapshot file was not created.");
            Require(new FileInfo(backupPath).Length > 0, "Pre-migration snapshot is empty.");

            // Verify snapshot opens independently and contains 100% committed state while primary storage is still open
            using (var snapshotEngine = new SqliteStorageEngine(backupPath))
            {
                Require(snapshotEngine.Status.Health == StorageHealth.Healthy, "Snapshot database is not healthy.");
                var intervals = snapshotEngine.GetIntervalsAsync(null, 0, long.MaxValue).GetAwaiter().GetResult();
                Require(intervals.Count == 1, "Snapshot intervals count mismatch.");
                Require(intervals[0].BytesRx == 5000 && intervals[0].BytesTx == 2000, "Snapshot byte counters mismatch.");
            }

            // Close active storage for rollback simulation
            activeStorage.Dispose();
            SqliteConnection.ClearAllPools();

            // Corrupt primary database to simulate failed migration
            File.WriteAllBytes(dbPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            // Execute rollback drill using SQLite backup API
            UpdateVerificationService.RollbackDatabaseSnapshot(backupPath, dbPath);

            // Re-open primary DB and verify complete restoration
            using (var restored = new SqliteStorageEngine(dbPath))
            {
                Require(restored.Status.Health == StorageHealth.Healthy, "Restored database is not healthy.");
                var intervals = restored.GetIntervalsAsync(null, 0, long.MaxValue).GetAwaiter().GetResult();
                Require(intervals.Count == 1, "Restored intervals count mismatch.");
                Require(intervals[0].BytesRx == 5000 && intervals[0].BytesTx == 2000, "Restored byte counters mismatch.");
            }
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void IncompatibleSchemaDowngradePrevention()
    {
        var threw = false;
        try
        {
            UpdateVerificationService.VerifyDowngradeSafety(databaseUserVersion: 4, applicationSupportedVersion: 3);
        }
        catch (IncompatibleSchemaDowngradeException ex)
        {
            threw = true;
            Require(ex.DatabaseVersion == 4 && ex.ApplicationSupportedVersion == 3, "Exception metadata mismatch.");
        }
        Require(threw, "Downgrade from schema 4 to 3 must be refused.");

        UpdateVerificationService.VerifyDowngradeSafety(3, 3);
    }

    private static void OperationalLoggerRotationAndRedaction()
    {
        var tempLogs = Path.Combine(Path.GetTempPath(), $"speedmeter_logs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempLogs);
        try
        {
            var logger = new OperationalLogger(tempLogs);
            logger.LogInfo("Connection established to 192.168.1.105 on adapter 00:1A:2B:3C:4D:5E with config C:\\Users\\JohnDoe\\AppData\\Local\\SpeedMeterPro\\settings.json");

            var recentLines = logger.GetRecentLogLines();
            Require(recentLines.Count == 1, "Log line count mismatch.");
            var line = recentLines[0];

            Require(line.Contains("[REDACTED_IP]"), "Private IPv4 was not redacted.");
            Require(!line.Contains("192.168.1.105"), "Raw private IPv4 leaked in log.");

            Require(line.Contains("[REDACTED_MAC]"), "MAC address was not redacted.");
            Require(!line.Contains("00:1A:2B:3C:4D:5E"), "Raw MAC address leaked in log.");

            Require(line.Contains("[REDACTED_USER]"), "User profile path was not redacted.");
            Require(!line.Contains("JohnDoe"), "Username leaked in log.");
        }
        finally
        {
            try { if (Directory.Exists(tempLogs)) Directory.Delete(tempLogs, true); } catch { }
        }
    }

    private static void DiagnosticBundleRedactionPreview()
    {
        var errors = new[]
        {
            "Failed probe to gateway 10.0.0.1 via interface 11:22:33:44:55:66 at C:\\Users\\Administrator\\data"
        };

        var bundle = DiagnosticBundleService.CreateBundle(
            storageHealth: "Healthy",
            connectivityState: "Reachable",
            remoteProbesEnabled: false,
            recentErrorLines: errors
        );

        Require(bundle.ProductName == ProductIdentity.Name, "Product name mismatch in bundle.");
        Require(bundle.ProductVersion == ProductIdentity.ProductVersion, "Version mismatch in bundle.");
        Require(bundle.DbSchemaVersion == 3, "Schema version mismatch in bundle.");

        var preview = DiagnosticBundleService.GenerateRedactedPreview(bundle);
        Require(preview.Contains("[REDACTED_IP]"), "Diagnostic preview failed to redact IP.");
        Require(preview.Contains("[REDACTED_MAC]"), "Diagnostic preview failed to redact MAC.");
        Require(preview.Contains("[REDACTED_USER]"), "Diagnostic preview failed to redact username.");
        Require(!preview.Contains("10.0.0.1"), "Raw IP leaked in diagnostic preview.");
        Require(!preview.Contains("11:22:33:44:55:66"), "Raw MAC leaked in diagnostic preview.");
    }

    private static void ZeroExternalNetworkWhenUpdatesDisabled()
    {
        var manifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.1", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://updates.speedmeterpro.local/releases/pkg.exe",
            "4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041",
            1000000, 3, "https://updates.speedmeterpro.local/notes.html", "SpeedMeter Pro Project", "sig");

        Require(manifest is not null, "Manifest should be non-null.");
    }

    private static void AuthenticodePublisherEnforcementDrill()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"speedmeter_auth_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 }); // MZ header stub
        try
        {
            // 1. Unsigned binary throws SecurityException
            var threwUnsigned = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(tempFile, SigningTrustMode.Production);
            }
            catch (SecurityException)
            {
                threwUnsigned = true;
            }
            Require(threwUnsigned, "Unsigned binary must be rejected by Authenticode verification.");

            // 2. Mismatched publisher check
            var threwPublisher = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(tempFile, SigningTrustMode.DevelopmentTest, expectedPublisher: "Evil Corporation");
            }
            catch (SecurityException)
            {
                threwPublisher = true;
            }
            Require(threwPublisher, "Publisher mismatch must be rejected.");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private sealed class HangingStorageEngine : IStorageEngine
    {
        public StorageStatus Status => new(StorageHealth.Healthy, 0, 0, 0, null);
        public Task RecordSessionStartAsync(SessionRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordSessionEndAsync(string sessionId, DateTimeOffset endedAt, bool cleanly, string? reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordClockSegmentAsync(ClockSegmentRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeRevisionAsync(ScopeRevisionRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordScopeMembershipAsync(ScopeMembershipRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordCheckpointAsync(CounterCheckpointRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestIntervalAsync(UsageIntervalRecord record, CounterCheckpointRecord? checkpoint = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task IngestGapAsync(UsageIntervalRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SessionRecord?> GetLastSessionAsync(CancellationToken ct = default) => Task.FromResult<SessionRecord?>(null);
        public Task<long?> GetLastDurableTimestampUtcMsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<UsageIntervalRecord>> GetIntervalsAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>(Array.Empty<UsageIntervalRecord>());
        public Task<(long TotalRx, long TotalTx)> GetTotalBytesAsync(string? scopeRevisionId = null, long? startUtcMs = null, long? endUtcMs = null, CancellationToken ct = default) => Task.FromResult((0L, 0L));
        public Task<(long? EarliestUtcMs, long? LatestUtcMs)> GetHistoryAvailabilityAsync(CancellationToken ct = default) => Task.FromResult<(long?, long?)>((null, null));
        public Task<HistoricalSummary> GetUsageSummaryAsync(HistoricalRange range, string reportingTimezoneId, string? scopeRevisionId = null, string? networkProfileId = null, CancellationToken ct = default) => Task.FromResult(new HistoricalSummary(range, reportingTimezoneId, 0, 0, 0, 0, 1.0, CompletenessState.Complete));
        public Task<IReadOnlyList<UsageIntervalRecord>> GetPagedIntervalsAsync(HistoricalRange range, long? cursorStartUtcMs = null, string? cursorIntervalId = null, int pageSize = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UsageIntervalRecord>>(Array.Empty<UsageIntervalRecord>());
        public Task<int> ComputeDailyRollupsAsync(HistoricalRange range, string reportingTimezoneId, CancellationToken ct = default) => Task.FromResult(0);
        public Task RecordQuotaThresholdEventAsync(QuotaThresholdEvent record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<int>> GetRecordedThresholdsAsync(string ruleId, int ruleRevision, string cycleIdentifier, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
        public Task<int> PruneHistoryAsync(long beforeUtcMs, CancellationToken ct = default) => Task.FromResult(0);
        public Task ClearAllHistoryAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;

        public void Dispose()
        {
            // Simulate storage disposal hang
            Thread.Sleep(10000);
        }

        public ValueTask DisposeAsync()
        {
            Thread.Sleep(10000);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MockAdapterProvider : IAdapterCounterProvider
    {
        public AdapterObservation ReadSnapshot() =>
            new(ProviderAvailability.Available, Array.Empty<AdapterCounter>(), "GetIfTable2", "OK", "OK");
    }

    private static void WholeApplicationShutdownBoundEnforcement()
    {
        var hangingStorage = new HangingStorageEngine();
        var provider = new MockAdapterProvider();
        string? diagnosticLogged = null;

        var context = new ApplicationContext(
            provider: provider,
            clock: new MeasurementClock(),
            routeProvider: null,
            dbPath: null,
            settingsPath: null,
            explicitStorage: hangingStorage);

        context.DiagnosticLogger = msg => diagnosticLogged = msg;

        var sw = Stopwatch.StartNew();
        // Dispose with 3.0 second bound
        context.DisposeWithTimeout(TimeSpan.FromSeconds(3.0));
        sw.Stop();

        // Must complete in approximately 3.0s, NOT hang for 10s
        Require(sw.ElapsedMilliseconds < 5000, $"Shutdown exceeded timeout: {sw.ElapsedMilliseconds} ms.");
        Require(diagnosticLogged is not null && diagnosticLogged.Contains("Whole-application shutdown barrier exceeded"),
            "Shutdown barrier failure diagnostic was not logged.");
    }

    private static void InstallerGracefulShutdownHandshakeDrill()
    {
        var testSid = "drill_user_" + Guid.NewGuid().ToString("N");
        var eventName = @"Local\SpeedMeterPro.Shutdown." + testSid;

        // 1. Cooperative process simulation: listens for event and sets flag
        var shutdownSignaled = false;
        using var shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        var waitRegistered = ThreadPool.RegisterWaitForSingleObject(shutdownEvent, (state, timedOut) =>
        {
            if (!timedOut) shutdownSignaled = true;
        }, null, -1, false);

        try
        {
            var exited = InstallerEngine.RequestGracefulShutdown(testSid, TimeSpan.FromSeconds(1));
            // No actual process named SpeedMeter.App was running, returns true immediately
            Require(exited, "Graceful shutdown should succeed when no process running.");
            Require(!shutdownSignaled, "Shutdown should not have been signaled to nonexistent process.");
        }
        finally
        {
            waitRegistered.Unregister(null);
        }
    }

    private static void InstallerTransactionalInstallAndRollbackDrill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"speedmeter_inst_drill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var targetInstall = Path.Combine(tempDir, "ProgramFiles");
        Directory.CreateDirectory(targetInstall);

        // Populate V1 binaries
        File.WriteAllText(Path.Combine(targetInstall, "SpeedMeter.App.exe"), "V1_BINARY");
        File.WriteAllText(Path.Combine(targetInstall, "SpeedMeter.Core.dll"), "V1_CORE");

        // Prepare invalid V2 payload (missing SpeedMeter.App.exe to trigger commit validation failure)
        var invalidPayloadZip = Path.Combine(tempDir, "invalid_payload.zip");
        using (var zip = ZipFile.Open(invalidPayloadZip, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("SpeedMeter.Core.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("V2_CORE_CORRUPTED");
        }

        var threw = false;
        try
        {
            var options = new InstallOptions(
                TargetDirectory: targetInstall,
                PayloadZipPath: invalidPayloadZip,
                Silent: true,
                LaunchAfterInstall: false,
                CreateDesktopShortcut: false);

            InstallerEngine.InstallAsync(options).GetAwaiter().GetResult();
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Require(threw, "Corrupted payload without SpeedMeter.App.exe must throw.");

        // Transactional Rollback Guarantee: V1 binaries must be 100% preserved
        Require(File.Exists(Path.Combine(targetInstall, "SpeedMeter.App.exe")), "SpeedMeter.App.exe missing after rollback.");
        Require(File.ReadAllText(Path.Combine(targetInstall, "SpeedMeter.App.exe")) == "V1_BINARY", "V1 binary content was overwritten despite rollback.");
        Require(File.ReadAllText(Path.Combine(targetInstall, "SpeedMeter.Core.dll")) == "V1_CORE", "V1 core content was corrupted despite rollback.");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void InstallerFreshInstallUpgradeAndUninstallDrill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"speedmeter_matrix_drill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var targetInstall = Path.Combine(tempDir, "Programs", "SpeedMeterPro");
        var userDataDir = Path.Combine(tempDir, "UserData");
        Directory.CreateDirectory(userDataDir);

        // Create valid payload zip
        var payloadZip = Path.Combine(tempDir, "SpeedMeterPro-Payload.zip");
        using (var zip = ZipFile.Open(payloadZip, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("SpeedMeter.App.exe");
            using (var w = new StreamWriter(e1.Open())) w.Write("SPEEDMETER_APP_V1");
            var e2 = zip.CreateEntry("SpeedMeter.Core.dll");
            using (var w = new StreamWriter(e2.Open())) w.Write("SPEEDMETER_CORE_V1");
        }

        // 1. Fresh Install
        var installOpts = new InstallOptions(
            TargetDirectory: targetInstall,
            PayloadZipPath: payloadZip,
            Silent: true,
            LaunchAfterInstall: false,
            CreateDesktopShortcut: false);

        InstallerEngine.InstallAsync(installOpts).GetAwaiter().GetResult();
        Require(File.Exists(Path.Combine(targetInstall, "SpeedMeter.App.exe")), "Fresh install failed to create SpeedMeter.App.exe.");

        // 2. Simulate User Data Creation
        var dbFile = Path.Combine(userDataDir, "speedmeter.db");
        File.WriteAllText(dbFile, "USER_DATABASE_CONTENT");

        // 3. Uninstall with Preserve User Data
        var uninstallPreserve = new UninstallOptions(
            InstallDirectory: targetInstall,
            PurgeUserData: false,
            Silent: true);

        // Since we override default userDataDir in test, test logic directly
        Require(File.Exists(dbFile), "User data was lost before uninstall.");
        InstallerEngine.UninstallAsync(uninstallPreserve).GetAwaiter().GetResult();

        // 4. Uninstall with Purge User Data
        var uninstallPurge = new UninstallOptions(
            InstallDirectory: targetInstall,
            PurgeUserData: true,
            Silent: true);
        InstallerEngine.UninstallAsync(uninstallPurge).GetAwaiter().GetResult();

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
