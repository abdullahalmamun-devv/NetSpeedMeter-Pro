using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public sealed record ConfigExportPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("exportedAtUtc")]
    public DateTimeOffset ExportedAtUtc { get; set; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0";

    [JsonPropertyName("settings")]
    public SpeedMeterSettings Settings { get; set; } = new();

    [JsonPropertyName("alertRules")]
    public List<AlertRule>? AlertRules { get; set; }
}

public sealed record ConfigImportResult(
    bool Success,
    SpeedMeterSettings? Settings,
    IReadOnlyList<AlertRule>? AlertRules,
    string Message,
    bool RemoteChecksSuppressed = false);

public interface IConfigExportImportService
{
    string ExportToJson(SpeedMeterSettings settings, IReadOnlyList<AlertRule>? alertRules = null);
    void ExportToFile(string filePath, SpeedMeterSettings settings, IReadOnlyList<AlertRule>? alertRules = null);
    ConfigImportResult ImportFromJson(string json, SpeedMeterSettings currentSettings);
    ConfigImportResult ImportFromFile(string filePath, SpeedMeterSettings currentSettings);
}

public sealed class ConfigExportImportService : IConfigExportImportService
{
    public const int CurrentExportSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ExportToJson(SpeedMeterSettings settings, IReadOnlyList<AlertRule>? alertRules = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();

        var payload = new ConfigExportPayload
        {
            SchemaVersion = CurrentExportSchemaVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            AppVersion = "1.0",
            Settings = settings,
            AlertRules = alertRules is not null ? new List<AlertRule>(alertRules) : null
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public void ExportToFile(string filePath, SpeedMeterSettings settings, IReadOnlyList<AlertRule>? alertRules = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);

        var json = ExportToJson(settings, alertRules);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{filePath}.tmp.{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
            }

            if (File.Exists(filePath))
            {
                File.Move(tempPath, filePath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
        }
    }

    public ConfigImportResult ImportFromJson(string json, SpeedMeterSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new ConfigImportResult(false, null, null, "Import content is empty.");
        }

        ConfigExportPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ConfigExportPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ConfigImportResult(false, null, null, $"Malformed JSON syntax: {ex.Message}");
        }

        if (payload is null)
        {
            return new ConfigImportResult(false, null, null, "Parsed configuration payload is null.");
        }

        if (payload.SchemaVersion <= 0 || payload.SchemaVersion > CurrentExportSchemaVersion)
        {
            return new ConfigImportResult(false, null, null,
                $"Incompatible configuration schema version {payload.SchemaVersion} (supported: 1..{CurrentExportSchemaVersion}).");
        }

        if (payload.Settings is null)
        {
            return new ConfigImportResult(false, null, null, "Configuration file is missing settings data.");
        }

        var imported = payload.Settings;
        imported.Sanitize();

        // CRITICAL PRIVACY INVARIANT: Never silently enable remote checks via import
        var remoteChecksSuppressed = false;
        if (imported.ConnectionChecksEnabled && !currentSettings.ConnectionChecksEnabled)
        {
            imported.ConnectionChecksEnabled = false;
            remoteChecksSuppressed = true;
        }

        // Public IP disclosure must always be accepted locally by the user
        imported.PublicIpDisclosureAccepted = false;

        // Validate AlertRules if present
        List<AlertRule>? validatedRules = null;
        if (payload.AlertRules is not null)
        {
            validatedRules = new List<AlertRule>();
            for (var i = 0; i < payload.AlertRules.Count; i++)
            {
                var rule = payload.AlertRules[i];
                try
                {
                    rule.Validate();
                    validatedRules.Add(rule);
                }
                catch (Exception ex)
                {
                    return new ConfigImportResult(false, null, null,
                        $"Alert rule at index {i} ('{rule.RuleId}') failed validation: {ex.Message}");
                }
            }
        }

        var message = remoteChecksSuppressed
            ? "Configuration imported successfully. Remote connection checks were kept disabled to protect privacy."
            : "Configuration imported successfully.";

        return new ConfigImportResult(true, imported, validatedRules, message, remoteChecksSuppressed);
    }

    public ConfigImportResult ImportFromFile(string filePath, SpeedMeterSettings currentSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(currentSettings);

        if (!File.Exists(filePath))
        {
            return new ConfigImportResult(false, null, null, $"File '{filePath}' does not exist.");
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new ConfigImportResult(false, null, null, $"Unable to read file: {ex.Message}");
        }

        return ImportFromJson(json, currentSettings);
    }
}
