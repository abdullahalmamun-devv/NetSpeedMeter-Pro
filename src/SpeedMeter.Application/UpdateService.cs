using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;

[assembly: InternalsVisibleTo("SpeedMeter.Checks")]

namespace SpeedMeter.Application;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Verifying, ReadyToInstall, Installing, Failed, Offline, Deferred }

public sealed record UpdateStatus(UpdateState State, string Message, UpdateManifest? Manifest = null,
    long DownloadedBytes = 0, long TotalBytes = 0, string? ReadyInstallerPath = null);

public interface IUpdateService
{
    UpdateStatus Status { get; }
    event Action<UpdateStatus>? StatusChanged;
    Task<UpdateStatus> CheckAsync(bool manual, CancellationToken cancellationToken = default);
    Task<UpdateStatus> DownloadAndVerifyAsync(CancellationToken cancellationToken = default);
    void InstallVerifiedUpdate();
}

/// <summary>Opt-in GitHub distribution with a pinned production signing authority.</summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    public const string ManifestUrl = "https://abdullahalmamun-devv.github.io/NetSpeedMeter-Pro/updates/rc.json";
    private const long MaxManifestBytes = 64 * 1024;
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan CheckCadence = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromHours(1);
    private readonly ISettingsService settings;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Uri manifestUri;
    private readonly string stagingDirectory;
    private readonly string? databasePath;
    private readonly SigningTrustMode trustMode;
    private readonly Action<string, SigningTrustMode, string> verifyAuthenticode;
    private readonly Action<string, IReadOnlyList<string>> launchInstaller;
    private readonly SemaphoreSlim operation = new(1, 1);
    private UpdateStatus status = new(UpdateState.Idle, "Updates have not been checked.");
    private string? verifiedInstallerPath;

    public UpdateStatus Status => status;
    public event Action<UpdateStatus>? StatusChanged;

    private bool IsLabLoopback => trustMode == SigningTrustMode.DevelopmentTest && manifestUri.IsLoopback;

#if SPEEDMETER_UPDATE_LAB
    private sealed record DevelopmentLabConfiguration(Uri ManifestUri, string StagingDirectory, string? DatabasePath);
    private static DevelopmentLabConfiguration? developmentLabConfiguration;

    /// <summary>
    /// Available only in a separately compiled development lab build. There is no
    /// CLI, settings, or environment-variable switch in the consumer binary.
    /// </summary>
    public static void ConfigureDevelopmentLab(Uri manifestUri, string labRoot, string? databasePath)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        var root = TestSandboxGuard.ValidateRoot(labRoot);
        if (!manifestUri.IsLoopback ||
            (manifestUri.Scheme != Uri.UriSchemeHttp && manifestUri.Scheme != Uri.UriSchemeHttps) ||
            manifestUri.UserInfo.Length != 0)
            throw new SecurityException("Development update manifest must use a loopback origin.");
        var staging = TestSandboxGuard.ValidateChild(Path.Combine(root, "update-staging"), root);
        var database = databasePath is null ? null : TestSandboxGuard.ValidateChild(databasePath, root);
        var configuration = new DevelopmentLabConfiguration(manifestUri, staging, database);
        if (Interlocked.CompareExchange(ref developmentLabConfiguration, configuration, null) is not null)
            throw new InvalidOperationException("Development update lab was already configured.");
    }

    private static DevelopmentLabConfiguration RequireDevelopmentLab() =>
        developmentLabConfiguration ?? throw new InvalidOperationException(
            "Development update lab must be configured before creating the application window.");

    public UpdateService(ISettingsService settings)
        : this(settings, null, RequireDevelopmentLab().StagingDirectory,
            RequireDevelopmentLab().DatabasePath, RequireDevelopmentLab().ManifestUri,
            SigningTrustMode.DevelopmentTest, null, null) { }
#else
    public UpdateService(ISettingsService settings)
        : this(settings, null, null, null, null, SigningTrustMode.Production, null, null) { }
#endif

    internal UpdateService(ISettingsService settings, HttpClient? http, string? stagingDirectory,
        string? databasePath, Uri? manifestUri, SigningTrustMode trustMode = SigningTrustMode.DevelopmentTest,
        Action<string, SigningTrustMode, string>? verifyAuthenticode = null,
        Action<string, IReadOnlyList<string>>? launchInstaller = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        ownsHttp = http is null;
        this.stagingDirectory = stagingDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedMeterPro", "staging");
        this.databasePath = databasePath;
        this.trustMode = trustMode;
        this.verifyAuthenticode = verifyAuthenticode ?? UpdateVerificationService.VerifyStagedPayloadAuthenticode;
        this.launchInstaller = launchInstaller ?? LaunchInstallerProcess;
        this.manifestUri = manifestUri ?? new Uri(ManifestUrl);
        if (this.manifestUri.Scheme != Uri.UriSchemeHttps &&
            !(IsLabLoopback && this.manifestUri.Scheme == Uri.UriSchemeHttp))
            throw new ArgumentException("Update manifest requires HTTPS.", nameof(manifestUri));
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd($"SpeedMeterPro/{ProductIdentity.ProductVersion}");
        CleanupOwnedStaging();
    }

    public async Task<UpdateStatus> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (status.State is UpdateState.ReadyToInstall or UpdateState.Installing) return Status;
        if (!manual && !settings.CurrentSettings.AutoUpdateCheckEnabled)
            return Publish(UpdateState.Deferred, "Automatic update checks are off.");
        var now = DateTimeOffset.UtcNow;
        var current = settings.CurrentSettings;
        if (!manual && ((current.LastSuccessfulUpdateCheckUtc is { } success && now - success < CheckCadence) ||
                        (current.LastUpdateCheckAttemptUtc is { } attempt && now - attempt < FailureBackoff)))
            return Status;

        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (status.State is UpdateState.ReadyToInstall or UpdateState.Installing) return Status;
            // Recheck consent after waiting for any earlier operation.
            if (!manual && !settings.CurrentSettings.AutoUpdateCheckEnabled)
                return Publish(UpdateState.Deferred, "Automatic update checks are off.");
            if (!manual && ((settings.CurrentSettings.LastSuccessfulUpdateCheckUtc is { } recentSuccess &&
                             DateTimeOffset.UtcNow - recentSuccess < CheckCadence) ||
                            (settings.CurrentSettings.LastUpdateCheckAttemptUtc is { } recentAttempt &&
                             DateTimeOffset.UtcNow - recentAttempt < FailureBackoff)))
                return Status;
            Publish(UpdateState.Checking, "Checking for updates…");
            settings.CurrentSettings.LastUpdateCheckAttemptUtc = DateTimeOffset.UtcNow;
            settings.Save(settings.CurrentSettings);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            VerifyResponseOrigin(response);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
                throw new SecurityException("Update manifest exceeds the size limit.");
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bounded = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (bounded.Length + read > MaxManifestBytes) throw new SecurityException("Update manifest exceeds the size limit.");
                bounded.Write(buffer, 0, read);
            }
            var manifest = UpdateVerificationService.ParseManifest(Encoding.UTF8.GetString(bounded.ToArray()), IsLabLoopback);
            if (!UpdateVerificationService.VerifyManifestSignature(manifest,
                    UpdateVerificationService.GetCanonicalManifestBytes(manifest), trustMode))
                throw new SecurityException("Update manifest signature verification failed.");
            if (!SemanticVersion.TryParse(manifest.Version, out var offered) ||
                !SemanticVersion.TryParse(manifest.MinSupportedVersion, out var minimum) ||
                !SemanticVersion.TryParse(ProductIdentity.ProductVersion, out var installed))
                throw new SecurityException("Update version is invalid.");
            if (installed!.CompareTo(minimum) < 0)
                throw new SecurityException("This update requires a newer base version.");
            if (manifest.Channel != "stable" && manifest.Channel != ProductIdentity.ReleaseChannel)
                throw new SecurityException("Update channel does not match this installation.");
            if (ProductIdentity.ReleaseChannel == "stable" && manifest.Channel != "stable")
                throw new SecurityException("Stable installations cannot receive prerelease updates.");
            if ((manifest.Channel == "stable" && offered!.IsPrerelease) ||
                (manifest.Channel == "rc" && !offered!.IsPrerelease))
                throw new SecurityException("Update version does not match its signed channel.");
            var packageUri = new Uri(manifest.PackageUrl);
            var expectedReleasePath = $"/abdullahalmamun-devv/NetSpeedMeter-Pro/releases/download/v{manifest.Version}/";
            if ((IsLabLoopback ? !SameOrigin(packageUri, manifestUri) :
                    !packageUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !packageUri.IsDefaultPort) ||
                packageUri.UserInfo.Length != 0 ||
                !packageUri.AbsolutePath.StartsWith(expectedReleasePath, StringComparison.Ordinal) ||
                !packageUri.AbsolutePath.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Update package is not an approved version-pinned installer URL.");
            if (manifest.PackageSizeBytes is <= 0 or > MaxPackageBytes || manifest.TargetDbSchemaVersion < ProductIdentity.DatabaseSchemaVersion)
                throw new SecurityException("Update package size or schema is outside the supported range.");

            settings.CurrentSettings.LastSuccessfulUpdateCheckUtc = DateTimeOffset.UtcNow;
            settings.Save(settings.CurrentSettings);
            return offered!.CompareTo(installed) > 0
                ? Publish(UpdateState.Available, $"SpeedMeter Pro {manifest.Version} is available.", manifest)
                : Publish(UpdateState.UpToDate, "You're up to date.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Publish(UpdateState.Deferred, "Update check cancelled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Publish(UpdateState.Offline, "Unable to check for updates. Check your connection and try again.");
        }
        catch (Exception)
        {
            return Publish(UpdateState.Failed, "Update verification failed. No update was installed.");
        }
        finally { operation.Release(); }
    }

    public async Task<UpdateStatus> DownloadAndVerifyAsync(CancellationToken cancellationToken = default)
    {
        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            var manifest = status.State == UpdateState.Available ? status.Manifest : null;
            if (manifest is null) return Publish(UpdateState.Failed, "Check for an available update first.");
            Directory.CreateDirectory(stagingDirectory);
            verifiedInstallerPath = null;
            CleanupOwnedStaging();
            tempPath = UpdateVerificationService.ValidateAndResolveStagingPath(stagingDirectory, Guid.NewGuid().ToString("N") + ".download");
            var uri = new Uri(manifest.PackageUrl);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            VerifyResponseOrigin(response);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } announced && announced != manifest.PackageSizeBytes)
                throw new SecurityException("Update package size does not match the signed manifest.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > manifest.PackageSizeBytes || total > MaxPackageBytes)
                        throw new SecurityException("Update package exceeds its signed size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    Publish(UpdateState.Downloading, $"Downloading update: {total * 100 / manifest.PackageSizeBytes}%", manifest, total, manifest.PackageSizeBytes);
                }
                if (total != manifest.PackageSizeBytes)
                    throw new SecurityException("Update package is incomplete.");
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            Publish(UpdateState.Verifying, "Verifying signed update…", manifest);
            using (var file = File.OpenRead(tempPath))
                if (!UpdateVerificationService.VerifyPackagePayload(file, manifest.PackageSha256))
                    throw new SecurityException("Update package hash mismatch.");
            verifyAuthenticode(tempPath, trustMode,
                UpdateTrustAnchor.ExpectedPublisher);
            if (manifest.TargetDbSchemaVersion > ProductIdentity.DatabaseSchemaVersion)
            {
                if (databasePath is null || !File.Exists(databasePath))
                    throw new InvalidOperationException("Database snapshot path is unavailable for schema update.");
                UpdateVerificationService.CreatePreMigrationSnapshot(databasePath,
                    Path.Combine(Path.GetDirectoryName(databasePath)!, "backups"));
            }
            var readyPath = UpdateVerificationService.ValidateAndResolveStagingPath(stagingDirectory,
                "SpeedMeterPro-" + manifest.Version + "-" + Guid.NewGuid().ToString("N") + "-Setup.exe");
            File.Move(tempPath, readyPath);
            tempPath = null;
            verifiedInstallerPath = readyPath;
            return Publish(UpdateState.ReadyToInstall, "Verified update is ready to install.", manifest,
                manifest.PackageSizeBytes, manifest.PackageSizeBytes, readyPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Publish(UpdateState.Available, "Download cancelled. You can update later.", status.Manifest);
        }
        catch (Exception)
        {
            return Publish(UpdateState.Failed, "Update download or verification failed. Your current installation is unchanged.");
        }
        finally
        {
            if (tempPath is not null) try { File.Delete(tempPath); } catch { }
            operation.Release();
        }
    }

    public void InstallVerifiedUpdate()
    {
        if (status.State != UpdateState.ReadyToInstall || verifiedInstallerPath is null ||
            status.ReadyInstallerPath != verifiedInstallerPath || !File.Exists(verifiedInstallerPath))
            throw new InvalidOperationException("No verified update is ready.");
        // Reverify after staging to close the interval between download and execution.
        var manifest = status.Manifest ?? throw new InvalidOperationException("Update manifest is unavailable.");
        try
        {
            // Deny write/delete sharing until Process.Start has created the installer image section.
            // WinVerifyTrust opens the same pathname read-only while this handle remains live.
            using var lockedInstaller = new FileStream(verifiedInstallerPath, FileMode.Open, FileAccess.Read,
                FileShare.Read);
            if (lockedInstaller.Length != manifest.PackageSizeBytes ||
                !UpdateVerificationService.VerifyPackagePayload(lockedInstaller, manifest.PackageSha256))
                throw new SecurityException("Staged update changed after verification.");
            verifyAuthenticode(verifiedInstallerPath, trustMode,
                UpdateTrustAnchor.ExpectedPublisher);
            // The current executable directory is the installed product location.
            // No install path from network metadata or mutable user settings is trusted.
            launchInstaller(verifiedInstallerPath,
                ["-Silent", "-LaunchAfterInstall", "-Dir", AppContext.BaseDirectory]);
            Publish(UpdateState.Installing, "Verified installer started. SpeedMeter Pro will close while the update runs.", manifest);
        }
        catch
        {
            Publish(UpdateState.Failed, "Unable to start the verified update. Your current installation is unchanged.");
            throw;
        }
    }

    private static void LaunchInstallerProcess(string installerPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var launchedInstaller = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Installer could not be launched.");
        launchedInstaller.Dispose();
    }

    private void VerifyResponseOrigin(HttpResponseMessage response)
    {
        var uri = response.RequestMessage?.RequestUri;
        if (uri is null || (IsLabLoopback ? !SameOrigin(uri, manifestUri) : uri.Scheme != Uri.UriSchemeHttps))
            throw new SecurityException("Update response left its approved transport origin.");
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        left.Scheme == right.Scheme && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port && left.UserInfo.Length == 0;

    private UpdateStatus Publish(UpdateState state, string message, UpdateManifest? manifest = null,
        long downloaded = 0, long total = 0, string? readyPath = null)
    {
        var next = new UpdateStatus(state, message, manifest, downloaded, total, readyPath);
        status = next;
        StatusChanged?.Invoke(next);
        return next;
    }

    private void CleanupOwnedStaging()
    {
        try
        {
            if (!Directory.Exists(stagingDirectory)) return;
            foreach (var oldFile in Directory.EnumerateFiles(stagingDirectory))
            {
                var name = Path.GetFileName(oldFile);
                if (name.EndsWith(".download", StringComparison.OrdinalIgnoreCase) ||
                    (name.StartsWith("SpeedMeterPro-", StringComparison.OrdinalIgnoreCase) &&
                     name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(oldFile); } catch { /* A running installer may still hold the file. */ }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
        operation.Dispose();
    }
}

/// <summary>SemVer 2.0 precedence for stable and release candidate updates.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
    private readonly int major, minor, patch;
    private readonly string[] prerelease;
    public bool IsPrerelease => prerelease.Length > 0;
    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
        => (this.major, this.minor, this.patch, this.prerelease) = (major, minor, patch, prerelease);

    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (text is null || text.Length > 128) return false;
        var m = Pattern.Match(text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var major) ||
            !int.TryParse(m.Groups[2].Value, out var minor) || !int.TryParse(m.Groups[3].Value, out var patch)) return false;
        var labels = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : Array.Empty<string>();
        foreach (var label in labels)
            if (label.Length == 0 || (label.Length > 1 && label[0] == '0' && IsNumeric(label))) return false;
        version = new SemanticVersion(major, minor, patch, labels);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var order = major.CompareTo(other.major);
        if (order != 0) return order;
        order = minor.CompareTo(other.minor);
        if (order != 0) return order;
        order = patch.CompareTo(other.patch);
        if (order != 0) return order;
        if (prerelease.Length == 0) return other.prerelease.Length == 0 ? 0 : 1;
        if (other.prerelease.Length == 0) return -1;
        for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            var leftNumeric = IsNumeric(prerelease[i]);
            var rightNumeric = IsNumeric(other.prerelease[i]);
            order = leftNumeric && rightNumeric ? CompareNumeric(prerelease[i], other.prerelease[i]) :
                leftNumeric ? -1 : rightNumeric ? 1 : string.CompareOrdinal(prerelease[i], other.prerelease[i]);
            if (order != 0) return order;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    private static bool IsNumeric(string label)
    {
        foreach (var c in label) if (c is < '0' or > '9') return false;
        return true;
    }

    private static int CompareNumeric(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0 ? length : string.CompareOrdinal(left, right);
    }
}
