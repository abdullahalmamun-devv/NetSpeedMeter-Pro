using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public enum PreviousRunOutcome { None, Clean, OsShutdown, KnownCrash, UncleanUnknown }

public sealed record CrashRecoveryInfo(PreviousRunOutcome Outcome, CrashReport? Report);

public sealed record CrashReport(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string ProductVersion,
    string ReleaseSourceSha,
    int DatabaseSchemaVersion,
    string WindowsVersion,
    string RuntimeVersion,
    string Source,
    string ExceptionType,
    string Message,
    string StackTrace,
    string StorageHealth,
    string CollectorState);

/// <summary>Local-only fatal incident capture. Never sends a report or contacts a service.</summary>
public sealed class CrashReportService
{
    private const int MaxReports = 12;
    private const int MaxReportBytes = 24 * 1024;
    private const int MaxMarkerBytes = 2048;
    private const int MaxTextChars = 6000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex SensitiveField = new(@"(?im)\b(ssid|password|passwd|token|secret|api[_-]?key|authorization|cookie)\s*[:=][^\r\n]*", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Url = new(@"(?i)\b(?:https?|ftp)://\S+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Ipv6 = new(@"(?i)(?<![0-9a-f:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f]{0,4}(?![0-9a-f:])", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Email = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex AbsolutePath = new("""(?i)(?:[A-Z]:\\|\\\\|/Users/|/home/)[^\s"'<>]+""", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private readonly object sync = new();
    private readonly string directory;
    private readonly string markerPath;
    private bool fatalCaptured;
    private string runId = "";

    private sealed record RunMarker(string RunId, PreviousRunOutcome Outcome, string? CrashId);

    public CrashReportService(string? directory = null)
    {
        this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedMeterPro", "crashes");
        markerPath = Path.Combine(this.directory, "last-run.json");
    }

    public CrashRecoveryInfo BeginRun()
    {
        lock (sync)
        {
            Directory.CreateDirectory(directory);
            var previous = ReadMarker();
            var outcome = previous is null ? PreviousRunOutcome.None : previous.Outcome == PreviousRunOutcome.UncleanUnknown
                ? PreviousRunOutcome.UncleanUnknown : previous.Outcome;
            // A prior running marker means the process ended without an observed cause.
            var report = outcome == PreviousRunOutcome.KnownCrash ? ReadReport(previous?.CrashId) : null;
            runId = Guid.NewGuid().ToString("N");
            WriteMarker(new RunMarker(runId, PreviousRunOutcome.UncleanUnknown, null));
            PruneReports();
            return new CrashRecoveryInfo(outcome, report);
        }
    }

    public CrashReport? CaptureFatal(Exception? exception, string source, string storageHealth = "unavailable", string collectorState = "unavailable")
    {
        lock (sync)
        {
            if (fatalCaptured) return null;
            fatalCaptured = true;
            try
            {
                Directory.CreateDirectory(directory);
                var report = new CrashReport(
                    Guid.NewGuid().ToString("N")[..12], DateTimeOffset.UtcNow,
                    ProductIdentity.ProductVersion, ProductIdentity.CommitSha,
                    ProductIdentity.DatabaseSchemaVersion, Environment.OSVersion.ToString(),
                    Environment.Version.ToString(), Sanitize(source, 80),
                    Sanitize(exception?.GetType().FullName ?? "UnknownException", 160),
                    Sanitize(exception?.Message ?? "No exception message available.", 1024),
                    Sanitize(exception?.StackTrace ?? "No stack trace available.", MaxTextChars),
                    Sanitize(storageHealth, 80), Sanitize(collectorState, 80));
                var json = JsonSerializer.Serialize(report, JsonOptions);
                if (Encoding.UTF8.GetByteCount(json) > MaxReportBytes) return null;
                var path = ReportPath(report.Id);
                WriteAtomic(path, json);
                WriteMarker(new RunMarker(runId, PreviousRunOutcome.KnownCrash, report.Id));
                PruneReports();
                return report;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }
    }

    public void MarkExit(bool osShutdown)
    {
        lock (sync)
        {
            if (fatalCaptured || string.IsNullOrEmpty(runId)) return;
            try { WriteMarker(new RunMarker(runId, osShutdown ? PreviousRunOutcome.OsShutdown : PreviousRunOutcome.Clean, null)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string FormatReport(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return $"SpeedMeter Pro diagnostic report\n\nCrash ID: {report.Id}\nUTC time: {report.OccurredAtUtc:O}\nVersion: {report.ProductVersion}\nRelease source SHA: {report.ReleaseSourceSha}\nDatabase schema: {report.DatabaseSchemaVersion}\nWindows: {report.WindowsVersion}\n.NET runtime: {report.RuntimeVersion}\nSource: {report.Source}\nException: {report.ExceptionType}\nMessage: {report.Message}\nStorage health: {report.StorageHealth}\nCollector state: {report.CollectorState}\n\nStack trace:\n{report.StackTrace}\n\nThis report is local. Review it before sharing.\n";
    }

    public static Uri CreateIssueUri(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        // Fixed destination and bounded metadata only. The diagnostic text is never put in the URL.
        var title = Uri.EscapeDataString($"Crash report {report.Id}");
        var body = Uri.EscapeDataString($"SpeedMeter Pro {report.ProductVersion}\nWindows {report.WindowsVersion}\nRelease source SHA {report.ReleaseSourceSha}\nCrash ID {report.Id}\n\nDescribe what happened. Attach the reviewed diagnostic file only if you choose to share it.");
        return new Uri($"https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/issues/new?title={title}&body={body}");
    }

    private static string Sanitize(string value, int maxChars)
    {
        try
        {
            var text = value.Length > MaxTextChars ? value[..MaxTextChars] : value;
            text = RedactionHelper.RedactText(text);
            text = SensitiveField.Replace(text, "$1=[REDACTED]");
            text = Url.Replace(text, "[REDACTED_URL]");
            text = Ipv6.Replace(text, "[REDACTED_IP]");
            text = Email.Replace(text, "[REDACTED_EMAIL]");
            text = AbsolutePath.Replace(text, "[REDACTED_PATH]");
            if (!string.IsNullOrWhiteSpace(Environment.UserName))
                text = text.Replace(Environment.UserName, "[REDACTED_USER]", StringComparison.OrdinalIgnoreCase);
            return text.Length > maxChars ? text[..maxChars] + "…" : text;
        }
        catch (RegexMatchTimeoutException) { return "[REDACTED]"; }
    }

    private RunMarker? ReadMarker()
    {
        try
        {
            if (!File.Exists(markerPath) || new FileInfo(markerPath).Length > MaxMarkerBytes) return null;
            return JsonSerializer.Deserialize<RunMarker>(File.ReadAllText(markerPath));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private CrashReport? ReadReport(string? id)
    {
        if (!IsValidId(id)) return null;
        try
        {
            var path = ReportPath(id!);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxReportBytes) return null;
            return JsonSerializer.Deserialize<CrashReport>(File.ReadAllText(path));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool IsValidId(string? id) => id is { Length: 12 } && id.All(Uri.IsHexDigit);
    private string ReportPath(string id) => Path.Combine(directory, $"crash-{id}.json");

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private void WriteMarker(RunMarker marker) => WriteAtomic(markerPath, JsonSerializer.Serialize(marker));

    private void PruneReports()
    {
        try
        {
            foreach (var temp in Directory.EnumerateFiles(directory, "crash-*.json.tmp")) File.Delete(temp);
            var files = new DirectoryInfo(directory).EnumerateFiles("crash-*.json")
                .OrderByDescending(file => file.CreationTimeUtc).ThenByDescending(file => file.Name).ToList();
            foreach (var file in files.Skip(MaxReports)) file.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
