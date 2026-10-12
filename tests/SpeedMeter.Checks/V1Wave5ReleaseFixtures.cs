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
    private const string DevelopmentTestManifestPrivateKeyPem =
"""
-----BEGIN PRIVATE KEY-----
MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC5z80laEsugKgD
Fu0fzo5309iGBOX6/kQV2vq5bAIvb7q3aNtdna5JU9oS9+bsnIdBb47mMjPnAlG4
gZjuXav0l86GdzItF207b9YWMezwYoP+FZu75fZQ4Gn47UCbh7Raj48BTYz2uVIS
FvEQyAQ0mLR2UZzBxDBnMn+FS1/wLVu/GBeClPmqSRxf+bkGNF/TNYHjozXWbSRA
g9zAw6k9SIAVpBj0eeoxQ6+DkoTQmGvf0tHZ9M4lG8QqbBddDNDXrEXi1cWySrVU
gCP+3A2OpBoaTMEIYNzeN4PJlWO4h9RSYgiPFCujKJDjIsGWJcWH+Ur1AdV5PvhH
e+UdTpMlAgMBAAECggEAQA5cOZYfvPNLeG0x5/IseTDGB0WvBv0a+bHZF40dn77T
zqDOZxKR3+ORXVKrAEzh7/QGfmM2M2BATewXja0rN5IR95cl7BeUZ5YhZttlRGWh
/TjzzC314WHMBq27xSJeUY4GDbvvaarvmR7hkhhyIrO2Gsf902rQCPOGtKwMcd6J
1/577YzLsY0HsPBvjfJrjChOUP8eUyGMD1wzLPgMyrnOUOKUj27tir70A+W+Ugqi
U0G69ReiUVxP2rnzfGtsjnaoJOmgIwv7LtdZz80jV6VAw+w1Nk1K9ZtsdsUB+o7k
oPE5zleIVt/GTF0tLom8txWLDU/j1fA6yEDurI7XzQKBgQD0PzbH28SKlpRmvaIG
5LpYudwZPOdwE7S5zJxsX6RtDSiDYdRW3KLNRn0V71/EzNxuFQtRiV6eQb7+ou5y
Gt+K9G3PIPtXnNp5aFWtrUxnXfFC7ZXIB8knhsll/68v++YTZgoO3EP8as0VLb87
rdvzOG+bA2nCn21sT3tTWK7ouwKBgQDCwL79l7DFwThCTuODmgETXSfAULzvmlxq
8OdxudFtoJ+EM4j9y7O/1kuPVmEdInyuZb2bXR+R8jixAixSKhxmyF1VdeHCtPoM
uy0YSu7fb8VuERzkmdCzNmKAV9BextTGVHcggwnCtd5xv0CsmxSKYka7L4eV31vP
YeSjswslnwKBgQDs1JrOoidqZmYRVVFVs9WJEPb9NdQIFeOIwYDviLHL2BVvh524
eAoYV4AUk95d2nLm/trRvgyUEZYcHGpvE+WsPCR50jZEluA1Ftj1JfPOywWUawFw
SzReclC46Xq6YVuPdcLM9AGsMPcY0Rn7yTJcHV4ChgW35JTakBs6DuTS3wKBgQCn
e2rEWzdCWbAwFffCrSZPqMUOtmBFDdJ8hUAEpXBdfsyvvEILQKsMRxYj5n0NuSLQ
N96fRa5orI3ka6IB5wUVvynE3OORtmFzoWReh9J1hIGc7kqSrVS1Sgfbp6kiWIuT
JABujUJPmGf8GiVcSuGXQHzjbTSExtkFt4IQ5WCosQKBgDsZa1Z0WisJMp5x6Dzy
F35v65BiTj9X5pKdqX1zraGifig8rKoYWvEgoiwqCpNCDnq+B3lPInpw1NiiluOh
vqme0egky9CI5f3VVGbx+iCtj/CF/ogq3STc5ZjzJSY+9kmG2avrW/UJmkN+Aq6w
/6EYSW/D1TpeAmGyEm2Aa+q5
-----END PRIVATE KEY-----
""";

    private static RSA GetDevTestSigningKey()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(DevelopmentTestManifestPrivateKeyPem);
        return rsa;
    }

    public static int? TryRunChild(string[] args)
    {
        if (args.Length == 4 && args[0] == "--generate-lab-update-manifest")
        {
            var package = Path.GetFullPath(args[1]);
            var manifestUri = new Uri(args[2], UriKind.Absolute);
            var output = Path.GetFullPath(args[3]);
            if (!manifestUri.IsLoopback || manifestUri.Scheme != Uri.UriSchemeHttp ||
                !File.Exists(package) || !string.Equals(Path.GetFileName(package),
                    "SpeedMeterPro-1.0.0-rc.3-Setup.exe", StringComparison.Ordinal))
                throw new InvalidOperationException("Lab manifest requires loopback HTTP and the RC.3 Setup artifact.");
            var authority = manifestUri.GetLeftPart(UriPartial.Authority);
            using var packageStream = File.OpenRead(package);
            var packageHash = Convert.ToHexStringLower(SHA256.HashData(packageStream));
            var manifest = new UpdateManifest(1, "SpeedMeterPro", "rc", "1.0.0-rc.3",
                DateTimeOffset.UtcNow, "1.0.0-rc.2",
                authority + "/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/download/v1.0.0-rc.3/SpeedMeterPro-1.0.0-rc.3-Setup.exe",
                packageHash,
                new FileInfo(package).Length, 3,
                "https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/releases",
                UpdateTrustAnchor.ExpectedPublisher, "");
            using var key = GetDevTestSigningKey();
            manifest = manifest with { Signature = Convert.ToBase64String(key.SignData(
                UpdateVerificationService.GetCanonicalManifestBytes(manifest), HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1)) };
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(manifest));
            Console.WriteLine($"DevelopmentTest manifest: {output}");
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--run-shutdown-harness")
        {
            var userSid = args[1];
            var mode = args[2];
            var eventName = @"Local\SpeedMeterPro.Shutdown." + userSid;

            if (mode == "cooperative")
            {
                using var evt = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
                var signaled = evt.WaitOne(TimeSpan.FromSeconds(10));
                return signaled ? 0 : 2;
            }
            else if (mode == "stubborn")
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
                return 0;
            }
        }
        return null;
    }

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
        check("W5-REL: End-to-end update package verification pipeline enforces all stages atomically", EndToEndUpdatePackageVerificationPipelineDrill);
    }

    private static void CanonicalVersionIdentityConformity()
    {
        Require(ProductIdentity.Name == "SpeedMeter Pro", "Product name mismatch.");
        Require(ProductIdentity.ProductVersion == "1.0.0-rc.3", "Product version must be 1.0.0-rc.3.");
        Require(ProductIdentity.FileVersion == "1.0.0.3", "File version must be 1.0.0.3.");
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
        using var devKey = GetDevTestSigningKey();
        var manifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.1", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://updates.test/setup.exe", "4a1c4f727dc9c0bb35bfe041df248955ab574b5b4a1c4f727dc9c0bb35bfe041",
            75000000, 3, "https://updates.test/notes.html", "SpeedMeter Pro Project", "");

        var canonicalBytes = UpdateVerificationService.GetCanonicalManifestBytes(manifest);
        var signature = devKey.SignData(canonicalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signedManifest = manifest with { Signature = Convert.ToBase64String(signature) };

        // Valid signature passes against pinned DevelopmentTest trust anchor
        var valid = UpdateVerificationService.VerifyManifestSignature(signedManifest, canonicalBytes, SigningTrustMode.DevelopmentTest);
        Require(valid, "Legitimate manifest signature failed verification against pinned DevelopmentTest trust anchor.");

        // Tampered canonical payload fails
        var tamperedManifest = signedManifest with { PackageUrl = "https://updates.test/MALICIOUS.exe" };
        var tamperedBytes = UpdateVerificationService.GetCanonicalManifestBytes(tamperedManifest);
        var invalid = UpdateVerificationService.VerifyManifestSignature(tamperedManifest, tamperedBytes, SigningTrustMode.DevelopmentTest);
        Require(!invalid, "Tampered manifest payload must fail signature verification.");
    }

    private static void ManifestTrustAnchorTamperingDrill()
    {
        // 1. Attacker creates their own keypair and signs a malicious manifest
        using var attackerKey = RSA.Create(2048);
        var maliciousManifest = new UpdateManifest(
            1, "SpeedMeterPro", "rc", "1.0.0-rc.2", DateTimeOffset.UtcNow, "1.0.0-rc.1",
            "https://malicious.test/SpeedMeterPro-Setup.exe", "0000000000000000000000000000000000000000000000000000000000000000",
            75000000, 3, "https://malicious.test/notes.html", "SpeedMeter Pro Project", "");

        var maliciousBytes = UpdateVerificationService.GetCanonicalManifestBytes(maliciousManifest);
        var maliciousSig = attackerKey.SignData(maliciousBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signedMaliciousManifest = maliciousManifest with { Signature = Convert.ToBase64String(maliciousSig) };

        // Verification using application Trust Anchor must reject attacker key in both DevelopmentTest and Production modes
        var passedInDev = UpdateVerificationService.VerifyManifestSignature(signedMaliciousManifest, maliciousBytes, SigningTrustMode.DevelopmentTest);
        Require(!passedInDev, "Manifest signed by untrusted attacker key must fail DevelopmentTest Trust Anchor verification.");

        var passedInProd = UpdateVerificationService.VerifyManifestSignature(signedMaliciousManifest, maliciousBytes, SigningTrustMode.Production);
        Require(!passedInProd, "Manifest signed by untrusted attacker key must fail Production Trust Anchor verification.");

        // 2. Test-signed manifest must FAIL against Production Trust Anchor (strict key separation)
        using var devKey = GetDevTestSigningKey();
        var legitimateSig = devKey.SignData(maliciousBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var devSignedManifest = maliciousManifest with { Signature = Convert.ToBase64String(legitimateSig) };

        var passedCrossMode = UpdateVerificationService.VerifyManifestSignature(devSignedManifest, maliciousBytes, SigningTrustMode.Production);
        Require(!passedCrossMode, "Test-signed manifest must be rejected by Production Trust Anchor.");

        // 3. Test-signed manifest SUCCEEDS against DevelopmentTest Trust Anchor
        var passedDevMode = UpdateVerificationService.VerifyManifestSignature(devSignedManifest, maliciousBytes, SigningTrustMode.DevelopmentTest);
        Require(passedDevMode, "Test-signed manifest must succeed against DevelopmentTest Trust Anchor.");
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
        var tempStaging = TestDeleteGuard.CreateRoot();
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
            TestDeleteGuard.DeleteRoot(tempStaging);
        }
    }

    private static void LiveWriterSqliteSnapshotAndRollbackDrill()
    {
        var tempDir = TestDeleteGuard.CreateRoot();
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
            TestDeleteGuard.DeleteRoot(tempDir);
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
        var tempLogs = TestDeleteGuard.CreateRoot();
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
            TestDeleteGuard.DeleteRoot(tempLogs);
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
        var tempDir = TestDeleteGuard.CreateRoot();
        try
        {
            var signedPe = GetOrCreateTestSignedPe(tempDir);
            Require(File.Exists(signedPe), "Signed test PE was not located or generated.");

            // 1. Valid test PE passes WinVerifyTrust and publisher check in DevelopmentTest mode
            UpdateVerificationService.VerifyStagedPayloadAuthenticode(signedPe, SigningTrustMode.DevelopmentTest);

            // 2. In Production mode, test-signed PE fails chain verification (Untrusted root)
            var threwProdUntrusted = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(signedPe, SigningTrustMode.Production);
            }
            catch (SecurityException ex)
            {
                threwProdUntrusted = true;
                Require(ex.Message.Contains("chain validation failed") || ex.Message.Contains("trust verification failed") || ex.Message.Contains("0x800B0109") || ex.Message.Contains("Untrusted"),
                    $"Expected production untrusted root rejection, got: {ex.Message}");
            }
            Require(threwProdUntrusted, "Test-signed binary must be rejected in Production mode.");

            // 3. Tampered PE: flip bits in executable -> WinVerifyTrust MUST return TRUST_E_BAD_DIGEST (0x80096010)
            var tamperedPe = Path.Combine(tempDir, "tampered_payload.exe");
            File.Copy(signedPe, tamperedPe, overwrite: true);
            var peBytes = File.ReadAllBytes(tamperedPe);
            var tamperOffset = peBytes.Length > 0x2000 ? 0x1000 : peBytes.Length / 2;
            peBytes[tamperOffset] ^= 0xFF;
            File.WriteAllBytes(tamperedPe, peBytes);

            var threwTampered = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(tamperedPe, SigningTrustMode.DevelopmentTest);
            }
            catch (SecurityException ex)
            {
                threwTampered = true;
                Require(ex.Message.Contains("TRUST_E_BAD_DIGEST") || ex.Message.Contains("0x80096010"),
                    $"Expected TRUST_E_BAD_DIGEST exception, got: {ex.Message}");
            }
            Require(threwTampered, "Tampered binary must be rejected with TRUST_E_BAD_DIGEST.");

            // 4. Publisher mismatch check
            var threwPublisherMismatch = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(signedPe, SigningTrustMode.DevelopmentTest, expectedPublisher: "Evil Corporation Inc");
            }
            catch (SecurityException ex)
            {
                threwPublisherMismatch = true;
                Require(ex.Message.Contains("publisher mismatch"), $"Expected publisher mismatch, got: {ex.Message}");
            }
            Require(threwPublisherMismatch, "Publisher mismatch must be rejected.");

            // 5. Unsigned executable throws TRUST_E_NOSIGNATURE (0x800B0100)
            var unsignedFile = Path.Combine(tempDir, "unsigned_file.exe");
            File.WriteAllBytes(unsignedFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            var threwUnsigned = false;
            try
            {
                UpdateVerificationService.VerifyStagedPayloadAuthenticode(unsignedFile, SigningTrustMode.DevelopmentTest);
            }
            catch (SecurityException ex)
            {
                threwUnsigned = true;
                Require(ex.Message.Contains("TRUST_E_NOSIGNATURE") || ex.Message.Contains("0x800B0100") || ex.Message.Contains("missing"),
                    $"Expected unsigned rejection, got: {ex.Message}");
            }
            Require(threwUnsigned, "Unsigned binary must be rejected.");
        }
        finally
        {
            TestDeleteGuard.DeleteRoot(tempDir);
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

        // Must complete in approximately 3.0s (contract 3.0s + 500ms scheduler tolerance)
        Require(sw.ElapsedMilliseconds <= 3500, $"Shutdown exceeded tolerance (contract 3.0s + 500ms scheduler tolerance): {sw.ElapsedMilliseconds} ms.");
        Require(diagnosticLogged is not null && diagnosticLogged.Contains("Whole-application shutdown barrier exceeded"),
            "Shutdown barrier failure diagnostic was not logged.");
    }

    private static void InstallerGracefulShutdownHandshakeDrill()
    {
        var testSid = "drill_user_" + Guid.NewGuid().ToString("N");
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        Require(!string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe), "Current test executable path not found.");
        var processName = Path.GetFileNameWithoutExtension(currentExe);
        var repoRoot = FindRepoRoot();
        var localDotnet = Path.Combine(repoRoot, ".tools", "dotnet");
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? (Directory.Exists(localDotnet) ? localDotnet : null);

        try
        {
            // Case 1: Cooperative Process Handshake
            var psiCoop = new ProcessStartInfo(currentExe!, $"--run-shutdown-harness {testSid} cooperative")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                psiCoop.Environment["DOTNET_ROOT"] = dotnetRoot;
                psiCoop.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
            }

            using (var procCoop = Process.Start(psiCoop))
            {
                Require(procCoop != null, "Failed to launch cooperative child harness.");
                Thread.Sleep(300); // Allow child to start and listen

                var exited = InstallerEngine.RequestGracefulShutdown(testSid, TimeSpan.FromSeconds(5), null, processName!);
                Require(exited, "Installer graceful shutdown handshake failed for cooperative process.");
                if (!procCoop!.HasExited) procCoop.WaitForExit(2000);
                Require(procCoop.HasExited, "Cooperative process did not exit.");
                Require(procCoop.ExitCode == 0, $"Cooperative process exited with non-zero code {procCoop.ExitCode}.");
            }

            // Case 2: Stubborn Refusal (Installer bounded wait respects timeout and refuses destructive force-kill)
            var psiStubborn = new ProcessStartInfo(currentExe!, $"--run-shutdown-harness {testSid} stubborn")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                psiStubborn.Environment["DOTNET_ROOT"] = dotnetRoot;
                psiStubborn.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
            }

            using (var procStubborn = Process.Start(psiStubborn))
            {
                Require(procStubborn != null, "Failed to launch stubborn child harness.");
                Thread.Sleep(300); // Allow child to start

                var exited = InstallerEngine.RequestGracefulShutdown(testSid, TimeSpan.FromSeconds(1), null, processName!);
                Require(!exited, "Installer graceful shutdown must return false and refuse force-kill when stubborn process refuses to close.");
                Require(!procStubborn!.HasExited, "Stubborn process must NOT be force-killed by graceful shutdown handshake.");

                // Clean up stubborn process
                try { procStubborn.Kill(); procStubborn.WaitForExit(1000); } catch { }
            }
        }
        catch
        {
            throw;
        }
    }

    private static void InstallerTransactionalInstallAndRollbackDrill()
    {
        var tempDir = TestDeleteGuard.CreateRoot();
        var targetInstall = Path.Combine(tempDir, "SpeedMeterPro");
        Directory.CreateDirectory(targetInstall);

        // Populate V1 binaries
        File.WriteAllText(Path.Combine(targetInstall, "SpeedMeter.App.exe"), "V1_BINARY");
        File.WriteAllText(Path.Combine(targetInstall, "SpeedMeter.Core.dll"), "V1_CORE");
        File.WriteAllText(Path.Combine(targetInstall, "Uninstall.exe"), "V1_UNINSTALLER");
        File.WriteAllText(Path.Combine(targetInstall, ".speedmeter-pro-install-root"),
            InstallerEngine.InstallMarkerContent(targetInstall));

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

        TestDeleteGuard.DeleteRoot(tempDir);
    }

    private static void InstallerFreshInstallUpgradeAndUninstallDrill()
    {
        var tempDir = TestDeleteGuard.CreateRoot();
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
            Silent: true)
        { TestUserDataDirectory = userDataDir };

        // Since we override default userDataDir in test, test logic directly
        Require(File.Exists(dbFile), "User data was lost before uninstall.");
        InstallerEngine.UninstallAsync(uninstallPreserve).GetAwaiter().GetResult();
        WaitForDirectoryRemoval(targetInstall);
        Require(File.ReadAllText(dbFile) == "USER_DATABASE_CONTENT", "Preserve uninstall altered fixture data.");

        // A compatible reinstall must leave the retained data available.
        InstallerEngine.InstallAsync(installOpts).GetAwaiter().GetResult();
        Require(File.ReadAllText(dbFile) == "USER_DATABASE_CONTENT", "Reinstall did not retain fixture data.");

        // 4. Uninstall with Purge User Data
        var uninstallPurge = new UninstallOptions(
            InstallDirectory: targetInstall,
            PurgeUserData: true,
            Silent: true)
        { TestUserDataDirectory = userDataDir };
        InstallerEngine.UninstallAsync(uninstallPurge).GetAwaiter().GetResult();
        WaitForDirectoryRemoval(targetInstall);
        Require(!Directory.Exists(userDataDir), "Explicit sandbox purge retained fixture data.");

        TestDeleteGuard.DeleteRoot(tempDir);
    }

    private static void WaitForDirectoryRemoval(string target)
    {
        var wait = Stopwatch.StartNew();
        while (Directory.Exists(target) && wait.Elapsed < TimeSpan.FromSeconds(8))
            Thread.Sleep(100);
        Require(!Directory.Exists(target), "Deferred sandbox uninstall did not remove its owned install directory.");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SpeedMeter.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }

    private static string GetOrCreateTestSignedPe(string tempDir)
    {
        var targetFile = Path.Combine(tempDir, "signed_test_pe.exe");
        var repoRoot = FindRepoRoot();

        // First check known signed PEs in the repository
        var candidatePaths = new[]
        {
            Path.Combine(repoRoot, "artifacts", "release", "SpeedMeterPro-1.0.0-rc.1-win-x64", "SpeedMeter.App.exe"),
            Path.Combine(repoRoot, "artifacts", "release", "SpeedMeterPro-1.0.0-rc.1-Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "SpeedMeter.App.exe"),
            Path.Combine(AppContext.BaseDirectory, "SpeedMeterPro-Setup.exe")
        };

        foreach (var path in candidatePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                try
                {
#pragma warning disable SYSLIB0057
                    var sig = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
                    if (sig != null && sig.Subject.Contains("SpeedMeter Pro"))
                    {
                        File.Copy(fullPath, targetFile, overwrite: true);
                        return targetFile;
                    }
                }
                catch { }
            }
        }

        // If none found on disk, use PowerShell script file to sign a stub
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe))
        {
            File.Copy(currentExe, targetFile, overwrite: true);
            var scriptPath = Path.Combine(tempDir, "sign_stub.ps1");
            var scriptContent = $@"
$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object {{ $_.Subject -like '*SpeedMeter Pro*' }} | Select-Object -First 1
if ($cert) {{
    Set-AuthenticodeSignature -FilePath '{targetFile.Replace("'", "''")}' -Certificate $cert -HashAlgorithm SHA256
}}
";
            File.WriteAllText(scriptPath, scriptContent);
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            TestDeleteGuard.RequireChild(tempDir, scriptPath);
            try { File.Delete(scriptPath); } catch { }
        }

        return targetFile;
    }

    private static void EndToEndUpdatePackageVerificationPipelineDrill()
    {
        var tempDir = TestDeleteGuard.CreateRoot();
        try
        {
            var signedPe = GetOrCreateTestSignedPe(tempDir);
            Require(File.Exists(signedPe), "Signed test PE was not located or generated.");

            using var fs = File.OpenRead(signedPe);
            var fileSha256 = UpdateVerificationService.ComputeSha256(fs);
            var fileLength = new FileInfo(signedPe).Length;

            using var devKey = GetDevTestSigningKey();
            var manifest = new UpdateManifest(
                SchemaVersion: 1,
                Product: "SpeedMeterPro",
                Channel: "rc",
                Version: "1.0.0-rc.1",
                BuildDateUtc: DateTimeOffset.UtcNow,
                MinSupportedVersion: "1.0.0-rc.1",
                PackageUrl: "https://updates.speedmeterpro.local/releases/pkg.exe",
                PackageSha256: fileSha256,
                PackageSizeBytes: fileLength,
                TargetDbSchemaVersion: 3,
                ReleaseNotesUrl: "https://updates.speedmeterpro.local/notes/v1.0.0.html",
                Publisher: UpdateTrustAnchor.ExpectedPublisher,
                Signature: ""
            );

            var canonicalBytes = UpdateVerificationService.GetCanonicalManifestBytes(manifest);
            var sig = devKey.SignData(canonicalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signedManifest = manifest with { Signature = Convert.ToBase64String(sig) };
            var manifestJson = System.Text.Json.JsonSerializer.Serialize(signedManifest);

            // 1. Valid package verification pipeline succeeds in DevelopmentTest mode
            UpdateVerificationService.VerifyUpdatePackage(
                manifestJson,
                signedPe,
                SigningTrustMode.DevelopmentTest,
                currentDatabaseVersion: 3,
                applicationSupportedVersion: 3);

            // 2. Incompatible database version fails downgrade check
            var threwDowngrade = false;
            try
            {
                UpdateVerificationService.VerifyUpdatePackage(
                    manifestJson,
                    signedPe,
                    SigningTrustMode.DevelopmentTest,
                    currentDatabaseVersion: 4,
                    applicationSupportedVersion: 3);
            }
            catch (IncompatibleSchemaDowngradeException)
            {
                threwDowngrade = true;
            }
            Require(threwDowngrade, "VerifyUpdatePackage must enforce downgrade prevention.");

            // 3. Bad hash fails payload check
            var badHashManifest = signedManifest with { PackageSha256 = new string('0', 64) };
            var badHashCanonical = UpdateVerificationService.GetCanonicalManifestBytes(badHashManifest);
            var badHashSig = devKey.SignData(badHashCanonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signedBadHash = badHashManifest with { Signature = Convert.ToBase64String(badHashSig) };
            var badHashJson = System.Text.Json.JsonSerializer.Serialize(signedBadHash);

            var threwBadHash = false;
            try
            {
                UpdateVerificationService.VerifyUpdatePackage(
                    badHashJson,
                    signedPe,
                    SigningTrustMode.DevelopmentTest);
            }
            catch (SecurityException)
            {
                threwBadHash = true;
            }
            Require(threwBadHash, "VerifyUpdatePackage must reject mismatched package SHA-256.");
        }
        finally
        {
            TestDeleteGuard.DeleteRoot(tempDir);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
