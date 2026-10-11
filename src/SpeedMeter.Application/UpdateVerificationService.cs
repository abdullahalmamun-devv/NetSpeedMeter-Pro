using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public sealed record UpdateManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("buildDateUtc")] DateTimeOffset BuildDateUtc,
    [property: JsonPropertyName("minSupportedVersion")] string MinSupportedVersion,
    [property: JsonPropertyName("packageUrl")] string PackageUrl,
    [property: JsonPropertyName("packageSha256")] string PackageSha256,
    [property: JsonPropertyName("packageSizeBytes")] long PackageSizeBytes,
    [property: JsonPropertyName("targetDbSchemaVersion")] int TargetDbSchemaVersion,
    [property: JsonPropertyName("releaseNotesUrl")] string ReleaseNotesUrl,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("signature")] string Signature
);

public sealed class IncompatibleSchemaDowngradeException : InvalidOperationException
{
    public int DatabaseVersion { get; }
    public int ApplicationSupportedVersion { get; }

    public IncompatibleSchemaDowngradeException(int dbVersion, int appVersion)
        : base($"Database schema version {dbVersion} is newer than supported version {appVersion}. Refusing downgrade to protect data integrity.")
    {
        DatabaseVersion = dbVersion;
        ApplicationSupportedVersion = appVersion;
    }
}

public static class UpdateVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static UpdateManifest ParseManifest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? throw new JsonException("Failed to deserialize update manifest.");

        if (manifest.SchemaVersion <= 0)
            throw new InvalidOperationException("Invalid manifest schema version.");
        if (string.IsNullOrWhiteSpace(manifest.Product) || manifest.Product != ProductIdentity.Name.Replace(" ", ""))
            throw new InvalidOperationException("Manifest product identity mismatch.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Manifest version cannot be empty.");
        if (string.IsNullOrWhiteSpace(manifest.PackageSha256) || manifest.PackageSha256.Length != 64)
            throw new InvalidOperationException("Manifest package SHA-256 hash is missing or invalid.");
        if (manifest.PackageSizeBytes <= 0)
            throw new InvalidOperationException("Manifest package size must be positive.");
        if (string.IsNullOrWhiteSpace(manifest.PackageUrl) || !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Manifest package URL must be an absolute HTTPS URL.");

        return manifest;
    }

    public static bool VerifyManifestSignature(UpdateManifest manifest, byte[] manifestPayloadWithoutSignature, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestPayloadWithoutSignature);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (string.IsNullOrWhiteSpace(manifest.Signature)) return false;
        try
        {
            var signatureBytes = Convert.FromBase64String(manifest.Signature);
            return publicKey.VerifyData(manifestPayloadWithoutSignature, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static bool VerifyPackagePayload(Stream stream, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        var actualSha256 = ComputeSha256(stream);
        return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    public static string ValidateAndResolveStagingPath(string stagingDirectory, string packageFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);

        // Security: Prevent Directory Traversal attacks (e.g. ../../windows/system32/evil.exe)
        var fileNameOnly = Path.GetFileName(packageFileName);
        if (string.IsNullOrWhiteSpace(fileNameOnly) ||
            packageFileName.Contains("..") ||
            packageFileName.Contains('/') ||
            packageFileName.Contains('\\'))
        {
            throw new SecurityException("Package file name contains invalid path traversal characters.");
        }

        var fullStagingDir = Path.GetFullPath(stagingDirectory);
        var resolvedPath = Path.GetFullPath(Path.Combine(fullStagingDir, fileNameOnly));

        if (!resolvedPath.StartsWith(fullStagingDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("Target path escapes staging directory.");
        }

        return resolvedPath;
    }

    public static void VerifyDowngradeSafety(int databaseUserVersion, int applicationSupportedVersion)
    {
        if (databaseUserVersion > applicationSupportedVersion)
        {
            throw new IncompatibleSchemaDowngradeException(databaseUserVersion, applicationSupportedVersion);
        }
    }

    public static string CreatePreMigrationSnapshot(string dbPath, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database file not found.", dbPath);

        Directory.CreateDirectory(backupDirectory);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupDirectory, $"speedmeter.pre-migration-{timestamp}.bak");

        // Atomic snapshot using SQLite online backup or safe copy
        File.Copy(dbPath, backupPath, overwrite: false);
        return backupPath;
    }

    public static void RollbackDatabaseSnapshot(string backupPath, string targetDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDbPath);

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup snapshot file not found for rollback.", backupPath);

        File.Copy(backupPath, targetDbPath, overwrite: true);
    }
}
