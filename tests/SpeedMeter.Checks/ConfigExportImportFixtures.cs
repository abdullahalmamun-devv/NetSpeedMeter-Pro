using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

public static class ConfigExportImportFixtures
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }

    public static void RunAll()
    {
        Console.WriteLine("--- Running Configuration Export/Import Verification Fixtures ---");

        TestExportJsonStructure();
        TestExportFileAtomic();
        TestImportValidRoundtrip();
        TestImportRejectsMalformedJson();
        TestImportRejectsFutureSchema();
        TestImportRejectsInvalidRule();
        TestImportPrivacyPreservationInvariant();
    }

    private static void TestExportJsonStructure()
    {
        var service = new ConfigExportImportService();
        var settings = new SpeedMeterSettings
        {
            DisplayUnits = "MB/s",
            DecimalPrecision = 1,
            SampleIntervalSeconds = 2.0,
            Theme = "Light",
            WidgetLayout = "CompactGraph"
        };

        var json = service.ExportToJson(settings);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert(root.GetProperty("schemaVersion").GetInt32() == 1,
            "PASS W4-CONFIG-EXPORT-JSON: schemaVersion must be 1.");
        Assert(root.TryGetProperty("exportedAtUtc", out var exportedAt) && exportedAt.GetString() is not null,
            "PASS W4-CONFIG-EXPORT-JSON: exportedAtUtc must be present.");
        Assert(root.GetProperty("appVersion").GetString() == "1.0",
            "PASS W4-CONFIG-EXPORT-JSON: appVersion must be 1.0.");
        Assert(root.GetProperty("settings").GetProperty("displayUnits").GetString() == "MB/s",
            "PASS W4-CONFIG-EXPORT-JSON: settings.displayUnits matches.");

        Console.WriteLine("PASS W4-CONFIG-EXPORT-JSON: exports valid JSON with schema version 1 and metadata");
    }

    private static void TestExportFileAtomic()
    {
        var service = new ConfigExportImportService();
        var testRoot = TestDeleteGuard.CreateRoot();
        var tempDir = Path.Combine(testRoot, "exports");
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "exported_config.json");

        try
        {
            var settings = new SpeedMeterSettings { DisplayUnits = "Mbps" };
            service.ExportToFile(targetFile, settings);

            Assert(File.Exists(targetFile), "Target file must exist after export.");
            var files = Directory.GetFiles(tempDir);
            Assert(files.Length == 1, "Temporary files must be cleaned up after atomic export.");

            var (loaded, warn) = SettingsStorage.Load(targetFile);
            Assert(warn is null, "Loaded export file must not have corruption warning.");
            Assert(loaded.DisplayUnits == "Mbps", "Loaded display units must match.");

            Console.WriteLine("PASS W4-CONFIG-EXPORT-FILE-ATOMIC: atomic file write succeeds and produces valid readable file");
        }
        finally
        {
            TestDeleteGuard.DeleteRoot(testRoot);
        }
    }

    private static void TestImportValidRoundtrip()
    {
        var service = new ConfigExportImportService();
        var current = new SpeedMeterSettings { ConnectionChecksEnabled = false };

        var settings = new SpeedMeterSettings
        {
            DisplayUnits = "MB/s",
            DecimalPrecision = 3,
            SampleIntervalSeconds = 0.5,
            Theme = "Dark",
            WidgetLayout = "Detailed",
            WidgetOpacity = 0.75,
            CloseToTray = true
        };

        var rule = new AlertRule(
            RuleId: "rule-1",
            Revision: 1,
            Kind: AlertKind.LocalDisconnect,
            ScopeId: "scope-1",
            NetworkId: "net-1",
            Enabled: true,
            TriggerThreshold: 1.0,
            RecoveryThreshold: 0.0,
            ObservationWindowMs: 5000,
            MinimumSamples: 1,
            PersistenceMs: 3000);

        var json = service.ExportToJson(settings, [rule]);
        var result = service.ImportFromJson(json, current);

        Assert(result.Success, "Import must succeed.");
        Assert(result.Settings is not null, "Imported settings must not be null.");
        Assert(result.Settings!.DisplayUnits == "MB/s", "Imported display units must match.");
        Assert(result.Settings.WidgetLayout == "Detailed", "Imported widget layout must match.");
        Assert(result.AlertRules is not null && result.AlertRules.Count == 1, "Alert rules must be preserved.");
        Assert(result.AlertRules![0].RuleId == "rule-1", "Imported alert rule ID must match.");

        Console.WriteLine("PASS W4-CONFIG-IMPORT-VALID-ROUNDTRIP: export/import roundtrip preserves settings and rules");
    }

    private static void TestImportRejectsMalformedJson()
    {
        var service = new ConfigExportImportService();
        var current = new SpeedMeterSettings();

        var resultEmpty = service.ImportFromJson("", current);
        Assert(!resultEmpty.Success, "Empty JSON must be rejected.");

        var resultBadSyntax = service.ImportFromJson("{ this is not json }", current);
        Assert(!resultBadSyntax.Success, "Bad JSON syntax must be rejected.");
        Assert(resultBadSyntax.Settings is null, "Settings must remain null on failure.");

        Console.WriteLine("PASS W4-CONFIG-IMPORT-REJECTS-MALFORMED: rejects empty and syntactically invalid JSON");
    }

    private static void TestImportRejectsFutureSchema()
    {
        var service = new ConfigExportImportService();
        var current = new SpeedMeterSettings();

        var futureJson = """
        {
            "schemaVersion": 99,
            "exportedAtUtc": "2026-10-12T00:00:00Z",
            "appVersion": "99.0",
            "settings": {
                "displayUnits": "Mbps"
            }
        }
        """;

        var result = service.ImportFromJson(futureJson, current);
        Assert(!result.Success, "Future schema version must be rejected.");
        Assert(result.Message.Contains("schema version 99"), "Rejection message must mention schema version.");

        Console.WriteLine("PASS W4-CONFIG-IMPORT-REJECTS-FUTURE-SCHEMA: rejects unsupported future schema versions");
    }

    private static void TestImportRejectsInvalidRule()
    {
        var service = new ConfigExportImportService();
        var current = new SpeedMeterSettings();

        var invalidRuleJson = """
        {
            "schemaVersion": 1,
            "exportedAtUtc": "2026-10-12T00:00:00Z",
            "appVersion": "1.0",
            "settings": {
                "displayUnits": "Mbps"
            },
            "alertRules": [
                {
                    "ruleId": "",
                    "revision": 0,
                    "kind": 0,
                    "scopeId": "scope-1",
                    "networkId": "net-1",
                    "enabled": true,
                    "triggerThreshold": 1.0,
                    "recoveryThreshold": 0.0,
                    "observationWindowMs": 0,
                    "minimumSamples": 0,
                    "persistenceMs": 0
                }
            ]
        }
        """;

        var result = service.ImportFromJson(invalidRuleJson, current);
        Assert(!result.Success, "Invalid alert rule must cause import rejection.");

        Console.WriteLine("PASS W4-CONFIG-IMPORT-REJECTS-INVALID-RULE: rejects invalid alert rule without corrupting state");
    }

    private static void TestImportPrivacyPreservationInvariant()
    {
        var service = new ConfigExportImportService();
        var current = new SpeedMeterSettings { ConnectionChecksEnabled = false };

        var privacyAttemptJson = """
        {
            "schemaVersion": 1,
            "exportedAtUtc": "2026-10-12T00:00:00Z",
            "appVersion": "1.0",
            "settings": {
                "displayUnits": "Mbps",
                "connectionChecksEnabled": true,
                "publicIpDisclosureAccepted": true
            }
        }
        """;

        var result = service.ImportFromJson(privacyAttemptJson, current);
        Assert(result.Success, "Valid JSON should import.");
        Assert(result.RemoteChecksSuppressed, "Remote checks must be flagged as suppressed.");
        Assert(result.Settings!.ConnectionChecksEnabled == false,
            "CRITICAL PRIVACY INVARIANT: ConnectionChecksEnabled must NOT be silently enabled via import.");
        Assert(result.Settings!.PublicIpDisclosureAccepted == false,
            "CRITICAL PRIVACY INVARIANT: PublicIpDisclosureAccepted must NOT be auto-accepted via import.");

        Console.WriteLine("PASS W4-CONFIG-IMPORT-PRIVACY-INVARIANT: never silently enables remote checks or public IP requests on import");
    }
}
