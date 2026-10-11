using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public sealed record DiagnosticBundle(
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("productVersion")] string ProductVersion,
    [property: JsonPropertyName("commitSha")] string CommitSha,
    [property: JsonPropertyName("releaseChannel")] string ReleaseChannel,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("osVersion")] string OsVersion,
    [property: JsonPropertyName("clrVersion")] string ClrVersion,
    [property: JsonPropertyName("architecture")] string Architecture,
    [property: JsonPropertyName("logicalProcessors")] int LogicalProcessors,
    [property: JsonPropertyName("dbSchemaVersion")] int DbSchemaVersion,
    [property: JsonPropertyName("storageHealth")] string StorageHealth,
    [property: JsonPropertyName("connectivityState")] string ConnectivityState,
    [property: JsonPropertyName("remoteProbesEnabled")] bool RemoteProbesEnabled,
    [property: JsonPropertyName("recentErrors")] IReadOnlyList<string> RecentErrors
);

public static class DiagnosticBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static DiagnosticBundle CreateBundle(
        string storageHealth,
        string connectivityState,
        bool remoteProbesEnabled,
        IReadOnlyList<string> recentErrorLines)
    {
        var redactedErrors = recentErrorLines
            .Select(RedactionHelper.RedactText)
            .ToList();

        return new DiagnosticBundle(
            ProductName: ProductIdentity.Name,
            ProductVersion: ProductIdentity.ProductVersion,
            CommitSha: ProductIdentity.CommitSha,
            ReleaseChannel: ProductIdentity.ReleaseChannel,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.ToString(),
            ClrVersion: Environment.Version.ToString(),
            Architecture: ProductIdentity.TargetArchitecture,
            LogicalProcessors: Environment.ProcessorCount,
            DbSchemaVersion: ProductIdentity.DatabaseSchemaVersion,
            StorageHealth: storageHealth,
            ConnectivityState: connectivityState,
            RemoteProbesEnabled: remoteProbesEnabled,
            RecentErrors: redactedErrors
        );
    }

    public static string SerializeBundleJson(DiagnosticBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return JsonSerializer.Serialize(bundle, JsonOptions);
    }

    public static string GenerateRedactedPreview(DiagnosticBundle bundle)
    {
        var json = SerializeBundleJson(bundle);
        return RedactionHelper.RedactText(json);
    }
}
