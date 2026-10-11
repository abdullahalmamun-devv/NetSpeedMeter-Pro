using System;
using System.Diagnostics;
using System.Security.Principal;

namespace SpeedMeter.Windows;

public sealed record ProcessIdentity(
    int ProcessId,
    DateTimeOffset StartTimeUtc,
    string ProcessName,
    string ImagePath)
{
    public static ProcessIdentity FromCurrentProcess()
    {
        using var cur = Process.GetCurrentProcess();
        return new ProcessIdentity(
            cur.Id,
            new DateTimeOffset(cur.StartTime.ToUniversalTime(), TimeSpan.Zero),
            cur.ProcessName,
            cur.MainModule?.FileName ?? "");
    }
}

public sealed record ApplicationIdentity(
    string AppId,
    string DisplayName,
    string PackageFamilyName,
    bool IsUwp,
    bool IsSystemShared);

public enum AttributionCompleteness
{
    Complete,
    PartialLoss,
    StandardUserRestricted,
    Unattributed,
    NotMeasured
}

public sealed record ProcessTrafficRecord(
    ProcessIdentity Process,
    ApplicationIdentity Application,
    ulong BytesReceived,
    ulong BytesSent,
    AttributionCompleteness Completeness);

public static class AppAttributionSpike
{
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPidRecycled(ProcessIdentity original, int currentPid, DateTimeOffset currentStartTimeUtc)
    {
        // Compares supplied identities only; no process lifecycle event stream is collected here.
        return original.ProcessId == currentPid && original.StartTimeUtc != currentStartTimeUtc;
    }

    /// <summary>
    /// No attribution provider is implemented or measured. Token elevation cannot establish
    /// provider availability, traffic coverage, event loss, or byte-attribution completeness.
    /// </summary>
    public static AttributionCompleteness DetermineProviderCapability() => AttributionCompleteness.NotMeasured;
}
