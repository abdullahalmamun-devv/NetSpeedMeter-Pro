using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public static class RedactionHelper
{
    private static readonly Regex Ipv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
        RegexOptions.Compiled);

    private static readonly Regex MacRegex = new(
        @"\b(?:[0-9A-Fa-f]{2}[:-]){5}(?:[0-9A-Fa-f]{2})\b",
        RegexOptions.Compiled);

    private static readonly Regex UserPathRegex = new(
        @"[a-zA-Z]:\\Users\\[^\\]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string RedactText(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        var redacted = input;
        redacted = UserPathRegex.Replace(redacted, @"C:\Users\[REDACTED_USER]");
        redacted = MacRegex.Replace(redacted, "[REDACTED_MAC]");
        redacted = Ipv4Regex.Replace(redacted, "[REDACTED_IP]");
        return redacted;
    }
}

public sealed class OperationalLogger
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB ceiling per file
    private const int MaxRotatedFiles = 3; // Keep up to 3 files (approx 7 days typical activity)
    private readonly string logDirectory;
    private readonly string primaryLogPath;
    private readonly object syncLock = new();

    public OperationalLogger(string? logDirectory = null)
    {
        this.logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpeedMeterPro", "logs");
        this.primaryLogPath = Path.Combine(this.logDirectory, "operational.log");
        Directory.CreateDirectory(this.logDirectory);
    }

    public void Log(string level, string message, Exception? ex = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'Z'");
        var fullMessage = ex is null ? message : $"{message} Exception: {ex.GetType().Name} - {ex.Message}";
        var redactedMessage = RedactionHelper.RedactText(fullMessage);
        var logLine = $"[{timestamp}] [{level}] {redactedMessage}{Environment.NewLine}";

        lock (syncLock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(primaryLogPath, logLine);
            }
            catch
            {
                // Never crash application due to logger failure
            }
        }
    }

    public void LogInfo(string message) => Log("INFO", message);
    public void LogWarning(string message) => Log("WARN", message);
    public void LogError(string message, Exception? ex = null) => Log("ERROR", message, ex);

    public IReadOnlyList<string> GetRecentLogLines(int maxLines = 100)
    {
        lock (syncLock)
        {
            try
            {
                if (!File.Exists(primaryLogPath)) return Array.Empty<string>();
                var lines = File.ReadAllLines(primaryLogPath);
                return lines.TakeLast(maxLines).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(primaryLogPath)) return;
        var info = new FileInfo(primaryLogPath);
        if (info.Length < MaxFileSizeBytes) return;

        try
        {
            for (int i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var current = Path.Combine(logDirectory, $"operational.{i}.log");
                var next = Path.Combine(logDirectory, $"operational.{i + 1}.log");
                if (File.Exists(current))
                {
                    if (File.Exists(next)) File.Delete(next);
                    File.Move(current, next);
                }
            }
            var firstRotated = Path.Combine(logDirectory, "operational.1.log");
            if (File.Exists(firstRotated)) File.Delete(firstRotated);
            File.Move(primaryLogPath, firstRotated);
        }
        catch
        {
            // Ignore rotation errors
        }
    }
}
