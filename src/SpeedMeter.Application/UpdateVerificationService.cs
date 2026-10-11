using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
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

public enum SigningTrustMode
{
    Production,
    DevelopmentTest
}

public static class UpdateTrustAnchor
{
    public const string ExpectedPublisher = "SpeedMeter Pro";
    public const string ExpectedCertSubject = "CN=SpeedMeter Pro Release Candidate Signing, O=SpeedMeter Pro, C=US";

    public const string DevelopmentTestManifestPublicKeyPem =
"""
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuc/NJWhLLoCoAxbtH86O
d9PYhgTl+v5EFdr6uWwCL2+6t2jbXZ2uSVPaEvfm7JyHQW+O5jIz5wJRuIGY7l2r
9JfOhncyLRdtO2/WFjHs8GKD/hWbu+X2UOBp+O1Am4e0Wo+PAU2M9rlSEhbxEMgE
NJi0dlGcwcQwZzJ/hUtf8C1bvxgXgpT5qkkcX/m5BjRf0zWB46M11m0kQIPcwMOp
PUiAFaQY9HnqMUOvg5KE0Jhr39LR2fTOJRvEKmwXXQzQ16xF4tXFskq1VIAj/twN
jqQaGkzBCGDc3jeDyZVjuIfUUmIIjxQroyiQ4yLBliXFh/lK9QHVeT74R3vlHU6T
JQIDAQAB
-----END PUBLIC KEY-----
""";

    public const string ProductionManifestPublicKeyPem =
"""
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA/Ks4LrR4Bt9LSPCmanjQ
x58aOuWGYvUMN/S+1lnsL5G/dpN80NAeEqeiFDSCRECwAD5n/jhrBfHTG5sgjwXL
0xksd/g+QfnVHdsIuiBnAm1ZSHGCUDhd68iLUjep8N/JFUOYBgNCgOHvrN5eUlwS
cIhQQ3wYeC+TH8sBYevds8Xt/hEIOEnylC1merdQ42/F0D0T/sQ7z2s/H/Xeoe/L
FE1YlBPIvAYXwL123FDiL2Ou2Bz+YC4sHO26sUqnT2QoeHFkYeOYRcVQZszs6wda
k2XOJ6PB3p1aFKOMraNr7Z/jOnJPd0l82V72KpG44SL7UocsBgNxWL3oTJY+kmVv
6QIDAQAB
-----END PUBLIC KEY-----
""";

    private static RSA? pinnedPublicKeyOverride;
    private static RSA? cachedProdKey;
    private static RSA? cachedDevKey;
    private static readonly object KeyLock = new();

    public static RSA GetPinnedPublicKey(SigningTrustMode mode = SigningTrustMode.Production)
    {
        if (pinnedPublicKeyOverride != null)
            return pinnedPublicKeyOverride;

        lock (KeyLock)
        {
            if (mode == SigningTrustMode.Production)
            {
                if (cachedProdKey == null)
                {
                    cachedProdKey = RSA.Create();
                    cachedProdKey.ImportFromPem(ProductionManifestPublicKeyPem);
                }
                return cachedProdKey;
            }
            else
            {
                if (cachedDevKey == null)
                {
                    cachedDevKey = RSA.Create();
                    cachedDevKey.ImportFromPem(DevelopmentTestManifestPublicKeyPem);
                }
                return cachedDevKey;
            }
        }
    }

    public static void SetPinnedPublicKeyForTesting(RSA key) => pinnedPublicKeyOverride = key;
    public static void ResetPinnedKeyForTesting() => pinnedPublicKeyOverride = null;
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

    public static byte[] GetCanonicalManifestBytes(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        // Canonical deterministic representation: exact pipe-delimited payload excluding signature
        var canonical = $"{manifest.SchemaVersion}|{manifest.Product}|{manifest.Channel}|{manifest.Version}|{manifest.BuildDateUtc.ToUnixTimeSeconds()}|{manifest.MinSupportedVersion}|{manifest.PackageUrl}|{manifest.PackageSha256}|{manifest.PackageSizeBytes}|{manifest.TargetDbSchemaVersion}|{manifest.Publisher}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public static bool VerifyManifestSignature(
        UpdateManifest manifest,
        byte[] manifestPayloadWithoutSignature,
        SigningTrustMode mode = SigningTrustMode.Production)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifestPayloadWithoutSignature);

        if (string.IsNullOrWhiteSpace(manifest.Signature)) return false;
        try
        {
            var keyToUse = UpdateTrustAnchor.GetPinnedPublicKey(mode);
            var signatureBytes = Convert.FromBase64String(manifest.Signature);
            return keyToUse.VerifyData(manifestPayloadWithoutSignature, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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

    public static void VerifyStagedPayloadAuthenticode(
        string payloadFilePath,
        SigningTrustMode mode = SigningTrustMode.Production,
        string? expectedPublisher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadFilePath);
        if (!File.Exists(payloadFilePath))
            throw new FileNotFoundException("Payload file not found for Authenticode verification.", payloadFilePath);

        if (OperatingSystem.IsWindows())
        {
            var trustResult = WinTrustNative.VerifyFile(payloadFilePath);

            if (trustResult == WinTrustNative.TRUST_E_BAD_DIGEST)
            {
                throw new SecurityException($"Authenticode hash mismatch (TRUST_E_BAD_DIGEST 0x80096010): The payload file '{Path.GetFileName(payloadFilePath)}' has been tampered with or corrupted.");
            }

            if (trustResult == WinTrustNative.TRUST_E_NOSIGNATURE || trustResult == WinTrustNative.TRUST_E_FORM_UNKNOWN)
            {
                throw new SecurityException($"Authenticode signature missing (TRUST_E_NOSIGNATURE 0x{trustResult:X8}) on '{Path.GetFileName(payloadFilePath)}'.");
            }

            if (mode == SigningTrustMode.Production)
            {
                if (trustResult != WinTrustNative.ERROR_SUCCESS)
                {
                    throw new SecurityException($"Production Authenticode trust verification failed with error code 0x{trustResult:X8}.");
                }
            }
            else // DevelopmentTest mode
            {
                if (trustResult != WinTrustNative.ERROR_SUCCESS && trustResult != WinTrustNative.CERT_E_UNTRUSTEDROOT)
                {
                    throw new SecurityException($"DevelopmentTest Authenticode verification failed with unexpected error code 0x{trustResult:X8}.");
                }
            }
        }

        X509Certificate2 cert;
        try
        {
#pragma warning disable SYSLIB0057
            var rawCert = X509Certificate.CreateFromSignedFile(payloadFilePath);
            cert = new X509Certificate2(rawCert);
#pragma warning restore SYSLIB0057
        }
        catch (Exception ex)
        {
            throw new SecurityException($"Authenticode signature missing or invalid on '{Path.GetFileName(payloadFilePath)}': {ex.Message}", ex);
        }

        var targetPublisher = expectedPublisher ?? UpdateTrustAnchor.ExpectedPublisher;
        if (!cert.Subject.Contains(targetPublisher, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Authenticode publisher mismatch. Expected publisher containing '{targetPublisher}', but certificate subject was '{cert.Subject}'.");
        }

        if (mode == SigningTrustMode.Production)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            var isChainValid = chain.Build(cert);
            if (!isChainValid)
            {
                var status = chain.ChainStatus.Length > 0 ? chain.ChainStatus[0].StatusInformation : "Untrusted root";
                throw new SecurityException($"Production Authenticode signature chain validation failed: {status}");
            }
        }
        else // DevelopmentTest mode
        {
            var isTestSubject = cert.Subject.Contains("SpeedMeter Pro Release Candidate Signing", StringComparison.OrdinalIgnoreCase) ||
                                cert.Subject.Contains("SpeedMeter Pro", StringComparison.OrdinalIgnoreCase);
            if (!isTestSubject)
            {
                throw new SecurityException("Development test-signing rejected: Certificate does not match SpeedMeter Pro test certificate.");
            }
        }
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

        // Official SQLite Online Backup API: atomic, consistent snapshot of live database including WAL
        var srcBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };
        using var srcConn = new SqliteConnection(srcBuilder.ToString());
        srcConn.Open();

        var destBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        using var destConn = new SqliteConnection(destBuilder.ToString());
        destConn.Open();

        srcConn.BackupDatabase(destConn);
        return backupPath;
    }

    public static void RollbackDatabaseSnapshot(string backupPath, string targetDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDbPath);

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup snapshot file not found for rollback.", backupPath);

        SqliteConnection.ClearAllPools();

        // Clean up broken/corrupted database and any orphaned WAL/SHM artifacts from failed migration
        var walPath = targetDbPath + "-wal";
        var shmPath = targetDbPath + "-shm";
        try { if (File.Exists(targetDbPath)) File.Delete(targetDbPath); } catch { }
        try { if (File.Exists(walPath)) File.Delete(walPath); } catch { }
        try { if (File.Exists(shmPath)) File.Delete(shmPath); } catch { }

        File.Copy(backupPath, targetDbPath, overwrite: true);
        SqliteConnection.ClearAllPools();
    }

    public static void VerifyUpdatePackage(
        string manifestJson,
        string packageFilePath,
        SigningTrustMode mode = SigningTrustMode.Production,
        int? currentDatabaseVersion = null,
        int? applicationSupportedVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFilePath);

        var manifest = ParseManifest(manifestJson);
        var canonicalBytes = GetCanonicalManifestBytes(manifest);
        if (!VerifyManifestSignature(manifest, canonicalBytes, mode))
        {
            throw new SecurityException("Update manifest signature verification failed against pinned trust anchor.");
        }

        using (var fs = File.OpenRead(packageFilePath))
        {
            if (!VerifyPackagePayload(fs, manifest.PackageSha256))
            {
                throw new SecurityException($"Package payload hash does not match manifest SHA-256 ({manifest.PackageSha256}).");
            }
        }

        VerifyStagedPayloadAuthenticode(packageFilePath, mode, manifest.Publisher);

        if (currentDatabaseVersion.HasValue && applicationSupportedVersion.HasValue)
        {
            VerifyDowngradeSafety(currentDatabaseVersion.Value, applicationSupportedVersion.Value);
        }
    }
}

public static class WinTrustNative
{
    private const string WinTrustDll = "wintrust.dll";

    public static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public const uint WTD_CHOICE_FILE = 1;
    public const uint WTD_UI_NONE = 2;
    public const uint WTD_REVOKE_NONE = 0;
    public const uint WTD_STATEACTION_IGNORE = 0;
    public const uint WTD_SAFER_FLAG = 0x00000100;
    public const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    public const uint ERROR_SUCCESS = 0x0;
    public const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    public const uint TRUST_E_FORM_UNKNOWN = 0x800B0003;
    public const uint TRUST_E_BAD_DIGEST = 0x80096010;
    public const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;

        public WINTRUST_FILE_INFO(string filePath)
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>();
            pcwszFilePath = filePath;
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;

        public WINTRUST_DATA(IntPtr pFileInfo)
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>();
            pPolicyCallbackData = IntPtr.Zero;
            pSIPClientData = IntPtr.Zero;
            dwUIChoice = WTD_UI_NONE;
            fdwRevocationChecks = WTD_REVOKE_NONE;
            dwUnionChoice = WTD_CHOICE_FILE;
            pFile = pFileInfo;
            dwStateAction = 1; // WTD_STATEACTION_VERIFY
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = null;
            dwProvFlags = 0;
            dwUIContext = 0;
        }
    }

    [DllImport(WinTrustDll, ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWVTData);

    public static uint VerifyFile(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return ERROR_SUCCESS;

        var fileInfo = new WINTRUST_FILE_INFO(filePath);
        var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(fileInfo));
        try
        {
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);
            var trustData = new WINTRUST_DATA(pFileInfo);
            var pTrustData = Marshal.AllocHGlobal(Marshal.SizeOf(trustData));
            try
            {
                Marshal.StructureToPtr(trustData, pTrustData, false);
                var actionId = ActionGenericVerifyV2;
                var result = WinVerifyTrust(new IntPtr(-1) /* INVALID_HANDLE_VALUE */, actionId, pTrustData);

                // Clean up state data
                trustData = Marshal.PtrToStructure<WINTRUST_DATA>(pTrustData);
                trustData.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                Marshal.StructureToPtr(trustData, pTrustData, false);
                WinVerifyTrust(new IntPtr(-1), actionId, pTrustData);

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pTrustData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pFileInfo);
        }
    }
}
