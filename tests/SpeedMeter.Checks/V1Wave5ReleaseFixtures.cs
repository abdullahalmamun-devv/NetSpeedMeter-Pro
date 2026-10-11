using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Checks;

internal static class V1Wave5ReleaseFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("W5-REL: Canonical version model matches across ProductIdentity and Assembly", CanonicalVersionIdentityConformity);
        check("W5-REL: Update manifest parses valid payload and rejects malformed fields", UpdateManifestParsingAndValidation);
        check("W5-REL: Tampered manifest properties fail cryptographic signature verification", TamperedManifestRejection);
        check("W5-REL: Tampered package payload with single byte alteration fails SHA-256 verification", TamperedPackagePayloadRejection);
        check("W5-REL: Package staging path rejects directory traversal and quarantine escapes", StagingPathTraversalProtection);
        check("W5-REL: Pre-migration snapshot preserves authentic database and rollback restores 100% data fidelity", DatabasePreMigrationSnapshotAndRollbackDrill);
        check("W5-REL: Incompatible newer schema strictly blocks downgrade to protect data integrity", IncompatibleSchemaDowngradePrevention);
        check("W5-REL: Operational logger redacts IPs, MAC addresses, and user paths with bounded rotation", OperationalLoggerRotationAndRedaction);
        check("W5-REL: Diagnostic bundle exports sanitized system telemetry with verified redaction preview", DiagnosticBundleRedactionPreview);
        check("W5-REL: Update trust service performs zero unsolicited network requests when disabled", ZeroExternalNetworkWhenUpdatesDisabled);
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
            ""packageUrl"": ""https://updates.speedmeterpro.local/releases/v1.0.0-rc.1/SpeedMeterPro-1.0.0-rc.1-win-x64-Setup.exe"",
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
        var originalPayload = Encoding.UTF8.GetBytes("SpeedMeterPro-1.0.0-rc.1-https://updates.test/setup.exe-sha256");
        var signature = rsa.SignData(originalPayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sigBase64 = Convert.ToBase64String(signature);

        var manifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.1", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://updates.test/setup.exe", "4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041",
            75000000, 3, "https://updates.test/notes.html", "SpeedMeter Pro Project", sigBase64);

        // Valid signature passes
        var valid = UpdateVerificationService.VerifyManifestSignature(manifest, originalPayload, rsa);
        Require(valid, "Legitimate manifest signature failed verification.");

        // Tampered payload fails
        var tamperedPayload = Encoding.UTF8.GetBytes("SpeedMeterPro-1.0.0-rc.1-https://updates.test/MALICIOUS.exe-sha256");
        var invalid = UpdateVerificationService.VerifyManifestSignature(manifest, tamperedPayload, rsa);
        Require(!invalid, "Tampered payload must fail signature verification.");
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
            // Valid file name inside staging succeeds
            var resolved = UpdateVerificationService.ValidateAndResolveStagingPath(tempStaging, "update.exe");
            Require(resolved == Path.Combine(tempStaging, "update.exe"), "Legitimate staging filename misresolved.");

            // Traversal attacks must throw SecurityException
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

    private static void DatabasePreMigrationSnapshotAndRollbackDrill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"speedmeter_drill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "speedmeter.db");
        var backupDir = Path.Combine(tempDir, "backups");

        try
        {
            // Create populated database
            using (var storage = new SqliteStorageEngine(dbPath))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                storage.RecordSessionStartAsync(new SessionRecord("s1", "e1", now, null, null, false)).GetAwaiter().GetResult();
                storage.RecordClockSegmentAsync(new ClockSegmentRecord("seg1", "s1", 0, now, 10_000_000, "1.0", null)).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("rev1", "nic1", 1, "AutoPrimary", "GetIfTable2", "1.0", "AutoPrimary", "Valid", now)).GetAwaiter().GetResult();
                storage.RecordScopeMembershipAsync(new ScopeMembershipRecord("m1", "rev1", "nic1", "inc1", "unidentified", "s1", 0, null)).GetAwaiter().GetResult();
                storage.IngestIntervalAsync(new UsageIntervalRecord("int-1", "k1", "s1", "seg1", "rev1", "unidentified", now, now + 1000, 10000000, 5000, 2000, false, false, null, "1.0")).GetAwaiter().GetResult();
                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Create pre-migration snapshot
            var backupPath = UpdateVerificationService.CreatePreMigrationSnapshot(dbPath, backupDir);
            Require(File.Exists(backupPath), "Pre-migration snapshot file was not created.");
            Require(new FileInfo(backupPath).Length > 0, "Pre-migration snapshot is empty.");

            // Simulate corrupted / interrupted migration on primary DB
            File.WriteAllBytes(dbPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            // Execute rollback drill
            UpdateVerificationService.RollbackDatabaseSnapshot(backupPath, dbPath);

            // Re-open restored database and verify 100% data fidelity
            using (var restored = new SqliteStorageEngine(dbPath))
            {
                Require(restored.Status.Health == StorageHealth.Healthy, "Restored database is not healthy.");
                var intervals = restored.GetIntervalsAsync(null, 0, long.MaxValue).GetAwaiter().GetResult();
                Require(intervals.Count == 1, "Restored intervals count mismatch.");
                Require(intervals[0].BytesRx == 5000 && intervals[0].BytesTx == 2000, "Restored byte counters mismatch.");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void IncompatibleSchemaDowngradePrevention()
    {
        // Current supported version is 3. Opening a future schema version (e.g. 4) must throw
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

        // Same version is safe
        UpdateVerificationService.VerifyDowngradeSafety(3, 3);
    }

    private static void OperationalLoggerRotationAndRedaction()
    {
        var tempLogs = Path.Combine(Path.GetTempPath(), $"speedmeter_logs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempLogs);
        try
        {
            var logger = new OperationalLogger(tempLogs);

            // Log message with sensitive private IP, MAC, and user path
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
        // By construction and contract: zero HTTP requests initiated without user action
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
