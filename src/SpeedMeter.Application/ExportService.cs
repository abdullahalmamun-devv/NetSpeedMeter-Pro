using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;
using SpeedMeter.Storage;

namespace SpeedMeter.Application;

public interface IExportService
{
    Task<string> ExportUsageIntervalsToCsvAsync(
        HistoricalRange range,
        string destinationFilePath,
        string reportingTimezoneId,
        string? scopeId = null,
        CancellationToken ct = default);

    Task<string> ExportUsageIntervalsToJsonAsync(
        HistoricalRange range,
        string destinationFilePath,
        string reportingTimezoneId,
        string? scopeId = null,
        CancellationToken ct = default);
}

public sealed class ExportService : IExportService
{
    private const string CurrentSchemaVersion = "1.0";
    private const string CurrentAppVersion = "0.1.0";

    private readonly IUsageHistoryService historyService;

    public ExportService(IUsageHistoryService historyService)
    {
        this.historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
    }

    public static string SanitizeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Spreadsheet Formula Injection Protection (CSV Injection):
        // If string starts with =, +, -, @, prefix with a single quote.
        var sanitized = value;
        if (sanitized.StartsWith('=') || sanitized.StartsWith('+') ||
            sanitized.StartsWith('-') || sanitized.StartsWith('@') ||
            sanitized.StartsWith('\t') || sanitized.StartsWith('\r'))
        {
            sanitized = "'" + sanitized;
        }

        // Standard CSV RFC 4180 escaping: wrap in quotes and escape internal quotes
        if (sanitized.Contains('"') || sanitized.Contains(',') || sanitized.Contains('\n') || sanitized.Contains('\r'))
        {
            sanitized = "\"" + sanitized.Replace("\"", "\"\"") + "\"";
        }

        return sanitized;
    }

    public async Task<string> ExportUsageIntervalsToCsvAsync(
        HistoricalRange range,
        string destinationFilePath,
        string reportingTimezoneId,
        string? scopeId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportingTimezoneId);

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(reportingTimezoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var summary = await historyService.GetCustomSummaryAsync(range, reportingTimezoneId, scopeId, null, ct).ConfigureAwait(false);

        var tempFilePath = destinationFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                // Metadata Disclosure Header
                await writer.WriteLineAsync("# SpeedMeter Pro Usage History Export").ConfigureAwait(false);
                await writer.WriteLineAsync($"# SchemaVersion: {CurrentSchemaVersion}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# AppVersion: {CurrentAppVersion}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# GeneratedAtUtc: {DateTimeOffset.UtcNow:O}").ConfigureAwait(false);
                await writer.WriteLineAsync("# RangeSemantics: [StartUtc, EndUtc) half-open").ConfigureAwait(false);
                await writer.WriteLineAsync($"# RangeStartUtc: {DateTimeOffset.FromUnixTimeMilliseconds(range.StartUtcMs):O}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# RangeEndUtc: {DateTimeOffset.FromUnixTimeMilliseconds(range.EndUtcMs):O}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# ReportingTimezone: {SanitizeCsvValue(reportingTimezoneId)}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# ScopeFilter: {SanitizeCsvValue(scopeId ?? "All")}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# TotalBytesRx: {summary.BytesRx}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# TotalBytesTx: {summary.BytesTx}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# TotalBytes: {summary.TotalBytes}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# CoverageRatio: {summary.CoverageRatio:F4}").ConfigureAwait(false);
                await writer.WriteLineAsync($"# CompletenessState: {summary.Completeness}").ConfigureAwait(false);
                await writer.WriteLineAsync("#").ConfigureAwait(false);

                // CSV Header Row
                await writer.WriteLineAsync("IntervalId,StartUtcMs,EndUtcMs,StartUtc,EndUtc,StartLocal,EndLocal,BytesRx,BytesTx,TotalBytes,IsCoarse,IsGap,GapReason,ScopeRevisionId,NetworkProfileId,TimingConfidence").ConfigureAwait(false);

                long? cursorStartUtcMs = null;
                string? cursorIntervalId = null;
                const int pageSize = 500;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = await historyService.GetPagedIntervalsAsync(range, cursorStartUtcMs, cursorIntervalId, pageSize, ct).ConfigureAwait(false);
                    if (page.Count == 0) break;

                    foreach (var record in page)
                    {
                        var startUtc = DateTimeOffset.FromUnixTimeMilliseconds(record.StartUtcMs);
                        var endUtc = DateTimeOffset.FromUnixTimeMilliseconds(record.EndUtcMs);
                        var startLocal = TimeZoneInfo.ConvertTime(startUtc, tz);
                        var endLocal = TimeZoneInfo.ConvertTime(endUtc, tz);
                        var totalBytes = record.BytesRx + record.BytesTx;

                        var line = string.Join(",",
                            SanitizeCsvValue(record.IntervalId),
                            record.StartUtcMs.ToString(CultureInfo.InvariantCulture),
                            record.EndUtcMs.ToString(CultureInfo.InvariantCulture),
                            startUtc.ToString("O"),
                            endUtc.ToString("O"),
                            startLocal.ToString("O"),
                            endLocal.ToString("O"),
                            record.BytesRx.ToString(CultureInfo.InvariantCulture),
                            record.BytesTx.ToString(CultureInfo.InvariantCulture),
                            totalBytes.ToString(CultureInfo.InvariantCulture),
                            record.IsCoarse ? "true" : "false",
                            record.IsGap ? "true" : "false",
                            SanitizeCsvValue(record.GapReason),
                            SanitizeCsvValue(record.ScopeRevisionId),
                            SanitizeCsvValue(record.NetworkProfileId),
                            SanitizeCsvValue(record.TimingConfidence));

                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }

                    var last = page[^1];
                    cursorStartUtcMs = last.StartUtcMs;
                    cursorIntervalId = last.IntervalId;
                }

                await writer.FlushAsync(ct).ConfigureAwait(false);
            }

            // Atomic file placement
            File.Move(tempFilePath, destinationFilePath, overwrite: true);
            return destinationFilePath;
        }
        catch
        {
            try
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
            catch { /* best effort cleanup */ }
            throw;
        }
    }

    public async Task<string> ExportUsageIntervalsToJsonAsync(
        HistoricalRange range,
        string destinationFilePath,
        string reportingTimezoneId,
        string? scopeId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportingTimezoneId);

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(reportingTimezoneId); }
        catch { tz = TimeZoneInfo.Utc; }

        var summary = await historyService.GetCustomSummaryAsync(range, reportingTimezoneId, scopeId, null, ct).ConfigureAwait(false);

        var tempFilePath = destinationFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                jsonWriter.WriteStartObject();

                jsonWriter.WriteString("schemaVersion", CurrentSchemaVersion);
                jsonWriter.WriteString("appVersion", CurrentAppVersion);
                jsonWriter.WriteString("generatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));

                jsonWriter.WriteStartObject("range");
                jsonWriter.WriteNumber("startUtcMs", range.StartUtcMs);
                jsonWriter.WriteNumber("endUtcMs", range.EndUtcMs);
                jsonWriter.WriteString("startUtc", DateTimeOffset.FromUnixTimeMilliseconds(range.StartUtcMs).ToString("O"));
                jsonWriter.WriteString("endUtc", DateTimeOffset.FromUnixTimeMilliseconds(range.EndUtcMs).ToString("O"));
                jsonWriter.WriteString("semantics", "[startUtcMs, endUtcMs) half-open");
                jsonWriter.WriteEndObject();

                jsonWriter.WriteString("reportingTimezone", reportingTimezoneId);
                if (scopeId is not null) jsonWriter.WriteString("scopeFilter", scopeId);
                else jsonWriter.WriteNull("scopeFilter");

                jsonWriter.WriteStartObject("summary");
                jsonWriter.WriteNumber("totalBytes", summary.TotalBytes);
                jsonWriter.WriteNumber("bytesRx", summary.BytesRx);
                jsonWriter.WriteNumber("bytesTx", summary.BytesTx);
                jsonWriter.WriteNumber("coverageRatio", summary.CoverageRatio);
                jsonWriter.WriteString("completenessState", summary.Completeness.ToString());
                jsonWriter.WriteNumber("observedDurationMs", summary.ObservedDurationMs);
                jsonWriter.WriteNumber("missingDurationMs", summary.MissingDurationMs);
                jsonWriter.WriteEndObject();

                jsonWriter.WriteStartArray("intervals");

                long? cursorStartUtcMs = null;
                string? cursorIntervalId = null;
                const int pageSize = 500;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = await historyService.GetPagedIntervalsAsync(range, cursorStartUtcMs, cursorIntervalId, pageSize, ct).ConfigureAwait(false);
                    if (page.Count == 0) break;

                    foreach (var record in page)
                    {
                        var startUtc = DateTimeOffset.FromUnixTimeMilliseconds(record.StartUtcMs);
                        var endUtc = DateTimeOffset.FromUnixTimeMilliseconds(record.EndUtcMs);
                        var startLocal = TimeZoneInfo.ConvertTime(startUtc, tz);
                        var endLocal = TimeZoneInfo.ConvertTime(endUtc, tz);

                        jsonWriter.WriteStartObject();
                        jsonWriter.WriteString("intervalId", record.IntervalId);
                        jsonWriter.WriteNumber("startUtcMs", record.StartUtcMs);
                        jsonWriter.WriteNumber("endUtcMs", record.EndUtcMs);
                        jsonWriter.WriteString("startUtc", startUtc.ToString("O"));
                        jsonWriter.WriteString("endUtc", endUtc.ToString("O"));
                        jsonWriter.WriteString("startLocal", startLocal.ToString("O"));
                        jsonWriter.WriteString("endLocal", endLocal.ToString("O"));
                        jsonWriter.WriteNumber("bytesRx", record.BytesRx);
                        jsonWriter.WriteNumber("bytesTx", record.BytesTx);
                        jsonWriter.WriteNumber("totalBytes", record.BytesRx + record.BytesTx);
                        jsonWriter.WriteBoolean("isCoarse", record.IsCoarse);
                        jsonWriter.WriteBoolean("isGap", record.IsGap);
                        if (record.GapReason is not null) jsonWriter.WriteString("gapReason", record.GapReason);
                        else jsonWriter.WriteNull("gapReason");
                        jsonWriter.WriteString("scopeRevisionId", record.ScopeRevisionId);
                        jsonWriter.WriteString("networkProfileId", record.NetworkProfileId);
                        jsonWriter.WriteString("timingConfidence", record.TimingConfidence);
                        jsonWriter.WriteEndObject();
                    }

                    var last = page[^1];
                    cursorStartUtcMs = last.StartUtcMs;
                    cursorIntervalId = last.IntervalId;
                }

                jsonWriter.WriteEndArray();
                jsonWriter.WriteEndObject();

                await jsonWriter.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempFilePath, destinationFilePath, overwrite: true);
            return destinationFilePath;
        }
        catch
        {
            try
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
            catch { /* best effort cleanup */ }
            throw;
        }
    }
}
