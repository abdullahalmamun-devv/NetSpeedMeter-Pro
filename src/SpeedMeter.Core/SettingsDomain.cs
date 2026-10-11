using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedMeter.Core;

/// <summary>
/// Root persistent settings model with schema versioning.
/// Fully isolated from the SQLite accounting ledger.
/// </summary>
public sealed class SpeedMeterSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("displayUnits")]
    public string DisplayUnits { get; set; } = "Mbps";

    [JsonPropertyName("decimalPrecision")]
    public int DecimalPrecision { get; set; } = 2;

    [JsonPropertyName("sampleIntervalSeconds")]
    public double SampleIntervalSeconds { get; set; } = 1.0;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Dark";

    [JsonPropertyName("widgetTopmost")]
    public bool WidgetTopmost { get; set; } = true;

    [JsonPropertyName("widgetLocked")]
    public bool WidgetLocked { get; set; } = false;

    [JsonPropertyName("widgetOpacity")]
    public double WidgetOpacity { get; set; } = 0.90;

    [JsonPropertyName("clickThrough")]
    public bool ClickThrough { get; set; } = false;

    [JsonPropertyName("widgetX")]
    public double? WidgetX { get; set; }

    [JsonPropertyName("widgetY")]
    public double? WidgetY { get; set; }

    [JsonPropertyName("widgetMonitorDeviceName")]
    public string? WidgetMonitorDeviceName { get; set; }

    [JsonPropertyName("widgetMonitorBounds")]
    public string? WidgetMonitorBounds { get; set; }

    [JsonPropertyName("scopeMode")]
    public string ScopeMode { get; set; } = "AutoPrimary";

    [JsonPropertyName("selectedAdapterId")]
    public string? SelectedAdapterId { get; set; }

    [JsonPropertyName("reportingTimezoneId")]
    public string ReportingTimezoneId { get; set; } = "UTC";

    [JsonPropertyName("weekStartDay")]
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;

    [JsonPropertyName("startupDelaySeconds")]
    public int StartupDelaySeconds { get; set; } = 0;

    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    [JsonPropertyName("lowResourceMode")]
    public bool LowResourceMode { get; set; } = false;

    [JsonPropertyName("connectionChecksEnabled")]
    public bool ConnectionChecksEnabled { get; set; } = false;

    [JsonPropertyName("activeQuotaRule")]
    public QuotaRule? ActiveQuotaRule { get; set; }

    /// <summary>
    /// Validates and sanitizes all preferences within safe operational limits.
    /// </summary>
    public void Sanitize()
    {
        if (SchemaVersion <= 0) SchemaVersion = CurrentSchemaVersion;

        // Display unit bounds
        if (DisplayUnits != "Mbps" && DisplayUnits != "MB/s" && DisplayUnits != "Auto")
        {
            DisplayUnits = "Mbps";
        }

        // Precision bounds (0..4)
        DecimalPrecision = Math.Clamp(DecimalPrecision, 0, 4);

        // Sampling interval bounds (0.5s .. 10.0s)
        if (double.IsNaN(SampleIntervalSeconds) || double.IsInfinity(SampleIntervalSeconds))
        {
            SampleIntervalSeconds = 1.0;
        }
        else
        {
            SampleIntervalSeconds = Math.Clamp(SampleIntervalSeconds, 0.5, 10.0);
        }

        // Opacity bounds (0.20 .. 1.00)
        if (double.IsNaN(WidgetOpacity) || double.IsInfinity(WidgetOpacity))
        {
            WidgetOpacity = 0.90;
        }
        else
        {
            WidgetOpacity = Math.Clamp(WidgetOpacity, 0.20, 1.00);
        }

        // Validate reporting timezone
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(ReportingTimezoneId);
        }
        catch
        {
            ReportingTimezoneId = "UTC";
        }

        // Startup delay bounds (0 .. 60)
        StartupDelaySeconds = Math.Clamp(StartupDelaySeconds, 0, 60);

        // Sanitize quota rule if present
        if (ActiveQuotaRule is not null)
        {
            try
            {
                ActiveQuotaRule.Validate();
            }
            catch
            {
                ActiveQuotaRule = null;
            }
        }
    }
}

/// <summary>
/// File-based atomic settings persistence with corruption recovery and schema migration support.
/// </summary>
public static class SettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads settings from the specified JSON file path.
    /// In case of corruption or unparseable content:
    ///   1. Preserves the corrupt file as [filePath].corrupt.[timestamp].json
    ///   2. Loads safe default settings
    ///   3. Does NOT crash the application or monitoring pipeline.
    /// </summary>
    public static (SpeedMeterSettings Settings, string? DiagnosticWarning) Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            var def = new SpeedMeterSettings();
            def.Sanitize();
            return (def, null);
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<SpeedMeterSettings>(json, JsonOptions);
            if (loaded is null)
            {
                throw new JsonException("Deserialized settings returned null.");
            }

            // CRITICAL DEF-B02: Reject incompatible future schema versions without clobbering file
            if (loaded.SchemaVersion > SpeedMeterSettings.CurrentSchemaVersion)
            {
                var warn = $"Incompatible future settings schema version {loaded.SchemaVersion} (current supported: {SpeedMeterSettings.CurrentSchemaVersion}). Preserved original file and loaded safe in-memory defaults.";
                var safeDefault = new SpeedMeterSettings();
                safeDefault.Sanitize();
                return (safeDefault, warn);
            }

            loaded.Sanitize();
            return (loaded, null);
        }
        catch (Exception ex)
        {
            var warn = $"Settings file corrupted ({ex.Message}). Preserved backup and loaded safe defaults.";
            try
            {
                var corruptBackup = $"{filePath}.corrupt.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json";
                File.Copy(filePath, corruptBackup, overwrite: true);
            }
            catch { }

            var safeDefault = new SpeedMeterSettings();
            safeDefault.Sanitize();
            return (safeDefault, warn);
        }
    }

    /// <summary>
    /// Persists settings atomically: writes to temporary file, flushes, and replaces target file.
    /// Prevents file truncation during crash or power loss.
    /// </summary>
    public static void SaveAtomic(string filePath, SpeedMeterSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);

        settings.Sanitize();

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{filePath}.tmp.{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
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
}
