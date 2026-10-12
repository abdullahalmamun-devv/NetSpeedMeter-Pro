using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SpeedMeter.Core;

[assembly: InternalsVisibleTo("SpeedMeter.Checks")]

namespace SpeedMeter.Installer;

public sealed class SpeedMeterRunningException(string message) : InvalidOperationException(message);

public sealed record InstallOptions(
    string? TargetDirectory = null,
    string? PayloadZipPath = null,
    Stream? PayloadStream = null,
    bool Silent = false,
    bool LaunchAfterInstall = false,
    bool? CreateDesktopShortcut = null,
    bool? StartWithWindows = null,
    Action<string>? Log = null
);

public sealed record UninstallOptions(
    string? InstallDirectory = null,
    bool PurgeUserData = false,
    bool Silent = false,
    Action<string>? Log = null
)
{
    // Deliberately absent from the public CLI/options contract. Test fixtures alone may
    // inject an isolated disposable root, and runtime validation still guards that seam.
    internal string? TestUserDataDirectory { get; init; }
}

public static class InstallerEngine
{
    private const int WmClose = 0x0010;
    private const string InstallMarkerName = ".speedmeter-pro-install-root";

    public static string GetDefaultInstallDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SpeedMeterPro");

    public static string GetDefaultUserDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedMeterPro");

    public static string GetCurrentUserSid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var sid = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrWhiteSpace(sid)) return sid;
            }
        }
        catch { }
        return Environment.UserName;
    }

    public static Process[] GetActiveInstances(string processName = "SpeedMeter.App")
    {
        int currentSessionId;
        try { currentSessionId = Process.GetCurrentProcess().SessionId; }
        catch { currentSessionId = -1; }

        var currentPid = Process.GetCurrentProcess().Id;
        var list = new List<Process>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                if (p.Id != currentPid && (currentSessionId == -1 || p.SessionId == currentSessionId))
                {
                    list.Add(p);
                }
            }
            catch
            {
                // Access denied or process exited
            }
        }
        return list.ToArray();
    }

    public static bool RequestGracefulShutdown(string userSid, TimeSpan timeout, Action<string>? log = null, string processName = "SpeedMeter.App")
    {
        log?.Invoke("Checking for running SpeedMeter Pro instances in current session...");
        var processes = GetActiveInstances(processName);
        if (processes.Length == 0) return true;

        log?.Invoke("Sending graceful shutdown request to active instance...");

        // Signal named shutdown event
        try
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\SpeedMeterPro.Shutdown." + userSid, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch { }

        // Also post WM_CLOSE to window
        try
        {
            var hWnd = FindWindow(null, "SpeedMeter Pro");
            if (hWnd != IntPtr.Zero)
            {
                PostMessage(hWnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }

        // Monotonic wait for processes to exit
        var sw = Stopwatch.StartNew();
        var signaled = false;
        while (sw.Elapsed < timeout)
        {
            var active = GetActiveInstances(processName);
            if (active.Length == 0)
            {
                log?.Invoke("SpeedMeter Pro exited gracefully.");
                return true;
            }

            if (!signaled)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(@"Local\SpeedMeterPro.Shutdown." + userSid, out var handle))
                    {
                        handle.Set();
                        handle.Dispose();
                        signaled = true;
                    }
                }
                catch { }
            }

            Thread.Sleep(100);
        }

        log?.Invoke("SpeedMeter Pro did not exit within graceful shutdown timeout.");
        return false;
    }

    public static async Task InstallAsync(InstallOptions options)
    {
        var log = options.Log ?? (_ => { });
        var requestedTarget = options.TargetDirectory ?? GetDefaultInstallDirectory();
        ValidateInstallTarget(requestedTarget);
        var targetDir = Path.GetFullPath(requestedTarget);
        ValidateExistingInstallTarget(targetDir);
        var labRoot = TryGetLabRoot(targetDir);
        var shutdownKey = labRoot is null ? GetCurrentUserSid() : Path.GetFileName(labRoot);

        log($"Beginning installation of SpeedMeter Pro {ProductIdentity.ProductVersion}...");
        log($"Target Directory: {targetDir}");

        // 1. Graceful Shutdown Handshake
        var isCanonicalTarget = SamePath(targetDir, GetDefaultInstallDirectory());
        var runningProcs = GetActiveInstancesForTarget(targetDir, labTarget: true);
        if (runningProcs.Length > 0)
        {
            var closed = RequestTargetGracefulShutdown(isCanonicalTarget || labRoot is not null ? shutdownKey : null,
                targetDir, TimeSpan.FromSeconds(5), log);
            if (!closed)
            {
                throw new SpeedMeterRunningException("SpeedMeter Pro is currently running and could not be closed gracefully. Installation stopped to prevent data corruption.");
            }
        }

        // 2. Prepare Directories and Staging
        Directory.CreateDirectory(targetDir);
        var stagingDir = Path.Combine(targetDir, $".staging_{Guid.NewGuid():N}");
        var backupDir = Path.Combine(targetDir, $".backup_{Guid.NewGuid():N}");
        var committedPaths = new List<string>();
        Directory.CreateDirectory(stagingDir);

        try
        {
            // 3. Extract Payload to Staging
            log("Extracting installation payload to staging...");
            if (options.PayloadStream is not null)
            {
                using var archive = new ZipArchive(options.PayloadStream, ZipArchiveMode.Read, leaveOpen: true);
                archive.ExtractToDirectory(stagingDir, overwriteFiles: true);
            }
            else if (!string.IsNullOrWhiteSpace(options.PayloadZipPath) && File.Exists(options.PayloadZipPath))
            {
                ZipFile.ExtractToDirectory(options.PayloadZipPath, stagingDir, overwriteFiles: true);
            }
            else
            {
                throw new FileNotFoundException("Installation payload zip not found or not provided.");
            }

            var appExeStaged = Path.Combine(stagingDir, "SpeedMeter.App.exe");
            if (!File.Exists(appExeStaged))
            {
                throw new InvalidDataException("Staged payload does not contain SpeedMeter.App.exe.");
            }

            // 4. Upgrade Backup Phase (if existing installation is present)
            var existingFiles = Directory.GetFiles(targetDir);
            var isUpgrade = Directory.EnumerateFileSystemEntries(targetDir)
                .Any(path => !SamePath(path, stagingDir));
            if (isUpgrade)
            {
                log("Backing up previous version for upgrade rollback protection...");
                Directory.CreateDirectory(backupDir);
                foreach (var file in existingFiles)
                {
                    var name = Path.GetFileName(file);
                    File.Move(file, Path.Combine(backupDir, name));
                }
                foreach (var dir in Directory.GetDirectories(targetDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(".staging") || dirName.StartsWith(".backup")) continue;
                    Directory.Move(dir, Path.Combine(backupDir, dirName));
                }
            }

            // 5. Commit Phase (Move staged files to target)
            log("Committing files to target installation directory...");
            foreach (var file in Directory.GetFiles(stagingDir))
            {
                var name = Path.GetFileName(file);
                var destination = Path.Combine(targetDir, name);
                File.Move(file, destination, overwrite: true);
                committedPaths.Add(destination);
            }
            foreach (var dir in Directory.GetDirectories(stagingDir))
            {
                var name = Path.GetFileName(dir);
                var destSubDir = Path.Combine(targetDir, name);
                if (Directory.Exists(destSubDir)) Directory.Delete(destSubDir, true);
                Directory.Move(dir, destSubDir);
                committedPaths.Add(destSubDir);
            }

            // Windows Settings depends on this executable and registry entry.
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                throw new FileNotFoundException("Setup executable is unavailable for uninstall registration.");
            var uninstallExePath = Path.Combine(targetDir, "Uninstall.exe");
            if (!Path.GetFullPath(currentExe).Equals(Path.GetFullPath(uninstallExePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, uninstallExePath, overwrite: true);
                committedPaths.Add(uninstallExePath);
            }

            var markerPath = Path.Combine(targetDir, InstallMarkerName);
            File.WriteAllText(markerPath, InstallMarkerContent(targetDir));
            committedPaths.Add(markerPath);

            var targetExe = Path.Combine(targetDir, "SpeedMeter.App.exe");
            // Sandbox installs used by fixtures/release drills must not replace the
            // real account's Start Menu, startup entry, or Windows Apps entry.
            if (Path.GetFullPath(targetDir).Equals(Path.GetFullPath(GetDefaultInstallDirectory()), StringComparison.OrdinalIgnoreCase))
            {
                log("Registering Start Menu shortcut and Windows uninstall entries...");
                var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
                var shortcutPath = Path.Combine(startMenuDir, "SpeedMeter Pro.lnk");
                CreateShortcut(shortcutPath, targetExe, targetDir, "SpeedMeter Pro - Precision Network Performance Monitor");

                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var desktopShortcut = Path.Combine(desktopDir, "SpeedMeter Pro.lnk");
                if (options.CreateDesktopShortcut == true)
                    CreateShortcut(desktopShortcut, targetExe, targetDir, "SpeedMeter Pro");
                else if (options.CreateDesktopShortcut == false && File.Exists(desktopShortcut))
                    File.Delete(desktopShortcut);

                RegisterUninstallKey(targetDir, targetExe);

                if (options.StartWithWindows is not null)
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (options.StartWithWindows == true)
                        runKey?.SetValue("SpeedMeterPro", $"\"{targetExe}\"", RegistryValueKind.String);
                    else
                        runKey?.DeleteValue("SpeedMeterPro", throwOnMissingValue: false);
                }
            }

            // Clean up staging & backup on success
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
            try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }
        }
        catch (Exception ex)
        {
            log($"Installation failed: {ex.Message}. Initiating transactional rollback...");
            // Rollback Phase
            var rollbackSucceeded = false;
            try
            {
                foreach (var path in committedPaths.AsEnumerable().Reverse())
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                }

                if (Directory.Exists(backupDir))
                {
                    // Restore previous version
                    foreach (var file in Directory.GetFiles(backupDir))
                    {
                        var name = Path.GetFileName(file);
                        File.Move(file, Path.Combine(targetDir, name), overwrite: true);
                    }
                    foreach (var dir in Directory.GetDirectories(backupDir))
                    {
                        var name = Path.GetFileName(dir);
                        var destDir = Path.Combine(targetDir, name);
                        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                        Directory.Move(dir, destDir);
                    }
                    log("Previous version restored successfully.");
                }
                rollbackSucceeded = true;
            }
            catch (Exception rollbackEx)
            {
                log($"Rollback error: {rollbackEx.Message}");
            }
            finally
            {
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                if (rollbackSucceeded)
                    try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }
            }

            throw;
        }

        // File installation is committed. Launch is a separate best-effort step and must
        // never enter the rollback catch after the previous version's backup is removed.
        try { log("Installation completed successfully!"); } catch { }
        if (options.LaunchAfterInstall)
        {
            try
            {
                var targetExe = Path.Combine(targetDir, "SpeedMeter.App.exe");
                log("Launching SpeedMeter Pro...");
                if (Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true }) is null)
                    throw new InvalidOperationException("Process launch returned no process.");
            }
            catch (Exception ex)
            {
                try { log($"Warning: Installation succeeded, but SpeedMeter Pro could not launch automatically: {ex.Message}"); }
                catch { }
            }
        }
    }

    public static Task UninstallAsync(UninstallOptions options)
    {
        var log = options.Log ?? (_ => { });
        var targetDir = Path.GetFullPath(options.InstallDirectory ?? GetDefaultInstallDirectory());
        var userDataDir = options.TestUserDataDirectory ?? GetDefaultUserDataDirectory();

        // Validation precedes shutdown, staging cleanup, registry edits, and purge.
        ValidateUninstallRequest(options);
        var labRoot = TryGetLabRoot(targetDir);
        var shutdownKey = labRoot is null ? GetCurrentUserSid() : Path.GetFileName(labRoot);

        log("Beginning uninstallation of SpeedMeter Pro...");

        // 1. Graceful shutdown check
        var isCanonicalTarget = SamePath(targetDir, GetDefaultInstallDirectory());
        var runningProcs = GetActiveInstancesForTarget(targetDir, labTarget: true);
        if (runningProcs.Length > 0)
        {
            var closed = RequestTargetGracefulShutdown(isCanonicalTarget || labRoot is not null ? shutdownKey : null,
                targetDir, TimeSpan.FromSeconds(5), log);
            if (!closed)
            {
                throw new SpeedMeterRunningException("SpeedMeter Pro is running and could not be closed gracefully. Uninstallation aborted.");
            }
        }

        // Delete selected data before unregistering the app so a failed purge remains retryable
        // from Windows Settings. Retained data is never touched except for downloaded packages.
        var isCanonicalInstall = SamePath(targetDir, GetDefaultInstallDirectory());
        var touchUserData = options.TestUserDataDirectory is not null || options.PurgeUserData || isCanonicalInstall;
        var updateStagingDir = Path.Combine(userDataDir, "staging");
        if (touchUserData && Directory.Exists(updateStagingDir) && !options.PurgeUserData)
        {
            try { QuarantineAndDelete(updateStagingDir, userDataDir, "staging"); }
            catch (Exception ex) { log($"Warning: Could not remove staged update packages: {ex.Message}"); }
        }

        Exception? purgeError = null;
        if (options.PurgeUserData)
        {
            log($"Purging all user data and historical databases at {userDataDir}...");
            if (Directory.Exists(userDataDir))
            {
                try { QuarantineAndDelete(userDataDir, Path.GetDirectoryName(userDataDir)!,
                    options.TestUserDataDirectory is null ? "SpeedMeterPro" : Path.GetFileName(userDataDir)); }
                catch (Exception ex)
                {
                    purgeError = ex;
                    log($"Warning: Local data cleanup did not complete: {ex.Message}");
                }
            }
        }
        else
        {
            log($"Preserving historical database and settings at: {userDataDir}");
        }

        // 2. Remove shell integration only for the canonical per-user install.
        if (isCanonicalInstall)
        {
            log("Removing application shortcuts and Windows entries...");
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            var startMenuShortcut = Path.Combine(startMenuDir, "SpeedMeter Pro.lnk");
            try { if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut); } catch { }
            var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedMeter Pro.lnk");
            try { if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut); } catch { }
            try
            {
                using var uninstallKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
                uninstallKey?.DeleteSubKeyTree("SpeedMeterPro", throwOnMissingSubKey: false);
            }
            catch { }
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("SpeedMeterPro", throwOnMissingValue: false);
            }
            catch { }
        }

        // 4. Remove program binaries. The installed Uninstall.exe is first relaunched
        // from a verified out-of-tree copy by Program, so deletion completes here.
        log("Removing application program files...");
        if (Directory.Exists(targetDir))
        {
            if (SameOrChild(targetDir, Environment.ProcessPath ?? ""))
                throw new InvalidOperationException("The installed uninstaller is still running. Start cleanup through its verified external helper.");
            ValidateUninstallRequest(options);
            QuarantineAndDelete(targetDir, Path.GetDirectoryName(targetDir)!,
                IsValidDisposableInstallTarget(targetDir) ? "install" : "SpeedMeterPro", requireOwnedInstall: true);
        }

        if (purgeError is not null)
            throw new IOException("SpeedMeter Pro was removed, but some local data could not be deleted. Check the setup log for the remaining folder.", purgeError);
        log("SpeedMeter Pro has been uninstalled.");
        return Task.CompletedTask;
    }

    internal static string InstallMarkerContent(string path) =>
        "SpeedMeterPro.InstallRoot.v1:" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();

    internal static void ValidateUninstallRequest(UninstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InstallDirectory is not null && !Path.IsPathFullyQualified(options.InstallDirectory))
            throw new InvalidOperationException("Install location must be absolute.");
        var target = Path.GetFullPath(options.InstallDirectory ?? GetDefaultInstallDirectory());
        var canonicalInstall = Path.GetFullPath(GetDefaultInstallDirectory());
        var realData = Path.GetFullPath(GetDefaultUserDataDirectory());
        ValidateInstallTarget(options.InstallDirectory ?? GetDefaultInstallDirectory());
        if (!Directory.Exists(target))
            throw new InvalidOperationException("Target installation does not exist.");
        var isOwned = IsOwnedInstall(target);
        if (!isOwned)
            throw new InvalidOperationException("Target is not a recognized SpeedMeter Pro installation.");

        if (options.TestUserDataDirectory is not null)
        {
            ValidateTestDataOverride(options.TestUserDataDirectory, target);
        }
        else if (options.PurgeUserData || SamePath(target, canonicalInstall))
        {
            if (options.PurgeUserData && IsDisposableTestPath(target))
                throw new InvalidOperationException("A test installation cannot purge real SpeedMeter Pro data.");
            RejectReparseComponents(realData);
            if (Directory.Exists(realData)) RejectReparseTree(realData);
        }
    }

    private static void ValidateExistingInstallTarget(string target)
    {
        if (!Directory.Exists(target) || !Directory.EnumerateFileSystemEntries(target).Any()) return;
        if (IsOwnedInstall(target)) return;
        if (SamePath(target, GetDefaultInstallDirectory()) &&
            File.Exists(Path.Combine(target, "Uninstall.exe")) &&
            File.Exists(Path.Combine(target, "SpeedMeter.App.exe"))) return;
        throw new InvalidOperationException("Install location already contains files that are not a recognized SpeedMeter Pro installation.");
    }

    private static bool IsOwnedInstall(string target)
    {
        var marker = Path.Combine(target, InstallMarkerName);
        return File.Exists(marker) &&
            File.Exists(Path.Combine(target, "Uninstall.exe")) &&
            File.Exists(Path.Combine(target, "SpeedMeter.App.exe")) &&
            string.Equals(File.ReadAllText(marker), InstallMarkerContent(target), StringComparison.Ordinal);
    }

    private static void ValidateInstallTarget(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Install location must be absolute.");
        foreach (var segment in path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
            if (segment is "." or "..")
                throw new InvalidOperationException("Install location must not contain dot segments.");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(target));
        var isProductLeaf = string.Equals(leaf, "SpeedMeterPro", StringComparison.OrdinalIgnoreCase);
        var isTestInstallLeaf = string.Equals(leaf, "install", StringComparison.OrdinalIgnoreCase) &&
            IsValidDisposableInstallTarget(target);
        if (!isProductLeaf && !isTestInstallLeaf)
            throw new InvalidOperationException("Install location must be a dedicated SpeedMeterPro directory.");
        var profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var realData = Path.GetFullPath(GetDefaultUserDataDirectory());

        if (SameOrChild(target, profile) || SameOrChild(target, realData) || SameOrChild(realData, target))
            throw new InvalidOperationException("Install location cannot contain the user profile or SpeedMeter Pro data.");
        RejectReparseComponents(target);
        if (Directory.Exists(target)) RejectReparseTree(target);
    }

    private static void QuarantineAndDelete(string source, string expectedParent, string expectedLeaf,
        bool requireOwnedInstall = false)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!SamePath(Path.GetDirectoryName(full)!, expectedParent) ||
            !string.Equals(Path.GetFileName(full), expectedLeaf, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup root is not the expected product directory.");
        RejectReparseComponents(full);
        RejectReparseTree(full);
        if (requireOwnedInstall && !IsOwnedInstall(full))
            throw new InvalidOperationException("Installation ownership changed before cleanup.");
        var quarantine = Path.Combine(expectedParent, $".{expectedLeaf}.removing.{Guid.NewGuid():N}");
        RejectReparseComponents(expectedParent);
        Directory.Move(full, quarantine);
        try
        {
            RejectReparseComponents(quarantine);
            RejectReparseTree(quarantine);
            DeleteTreeChecked(quarantine, expectedParent);
        }
        catch (Exception ex)
        {
            throw new IOException($"Cleanup could not finish; remaining files may be in {quarantine}.", ex);
        }
    }

    private static void DeleteTreeChecked(string root, string expectedParent)
    {
        if (!SamePath(Path.GetDirectoryName(root)!, expectedParent))
            throw new InvalidOperationException("Cleanup escaped its product parent.");
        var pending = new Stack<(string Path, bool Visited)>();
        pending.Push((root, false));
        while (pending.Count > 0)
        {
            var (current, visited) = pending.Pop();
            if (!SameOrChild(root, current))
                throw new InvalidOperationException("Cleanup escaped its quarantine root.");
            RejectReparseComponents(current);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A reparse point blocks cleanup.");
            if ((attributes & FileAttributes.Directory) == 0)
            {
                File.Delete(current);
                continue;
            }
            if (visited)
            {
                Directory.Delete(current, recursive: false);
                continue;
            }
            pending.Push((current, true));
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                if (!SameOrChild(root, child))
                    throw new InvalidOperationException("Cleanup child escaped its quarantine root.");
                pending.Push((child, false));
            }
        }
    }

    private static void ValidateTestDataOverride(string dataPath, string target)
    {
        if (!Path.IsPathFullyQualified(dataPath))
            throw new InvalidOperationException("Test data path must be absolute.");
        var data = Path.GetFullPath(dataPath);
        var root = Path.GetDirectoryName(data)
            ?? throw new InvalidOperationException("Test data path has no disposable parent.");
        TestSandboxGuard.ValidateRoot(root);
        TestSandboxGuard.ValidateChild(dataPath, root);
        TestSandboxGuard.ValidateChild(target, root);
        TestSandboxGuard.ValidateTreeForDeletion(data, root);
        TestSandboxGuard.ValidateTreeForDeletion(target, root);
    }

    private static string? TryGetLabRoot(string target)
    {
#if SPEEDMETER_UPDATE_LAB
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SpeedMeterPro.Tests"));
        var relative = Path.GetRelativePath(parent, target);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        if (Guid.TryParseExact(first, "N", out _))
        {
            var root = TestSandboxGuard.ValidateRoot(Path.Combine(parent, first));
            TestSandboxGuard.ValidateChild(target, root);
            return root;
        }
#endif
        return null;
    }

    private static Process[] GetActiveInstancesForTarget(string target, bool labTarget)
    {
        var active = GetActiveInstances("SpeedMeter.App");
        if (!labTarget) return active;
        var expectedExe = Path.GetFullPath(Path.Combine(target, "SpeedMeter.App.exe"));
        var matched = new List<Process>();
        foreach (var process in active)
        {
            try
            {
                if (SamePath(process.MainModule?.FileName ?? "", expectedExe)) matched.Add(process);
            }
            catch { /* Do not signal an unidentifiable process during a lab run. */ }
        }
        return matched.ToArray();
    }

    private static bool RequestTargetGracefulShutdown(string? key, string target, TimeSpan timeout, Action<string>? log)
    {
        log?.Invoke("Requesting target installation shutdown...");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (GetActiveInstancesForTarget(target, labTarget: true).Length == 0) return true;
            if (key is not null) try
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\SpeedMeterPro.Shutdown." + key, out var handle))
                {
                    handle.Set();
                    handle.Dispose();
                }
            }
            catch { }
            foreach (var process in GetActiveInstancesForTarget(target, labTarget: true))
            {
                try
                {
                    if (process.MainWindowHandle != nint.Zero)
                        PostMessage(process.MainWindowHandle, WmClose, nint.Zero, nint.Zero);
                }
                catch { }
            }
            Thread.Sleep(100);
        }
        return GetActiveInstancesForTarget(target, labTarget: true).Length == 0;
    }

    private static bool IsValidDisposableInstallTarget(string target)
    {
        try
        {
            var root = TestSandboxGuard.ValidateRoot(Path.GetDirectoryName(target)!);
            TestSandboxGuard.ValidateChild(target, root);
            return true;
        }
        catch { return false; }
    }

    private static bool IsDisposableTestPath(string target)
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var sharedTestsParent = Path.Combine(temp, "SpeedMeterPro.Tests");
        if (SameOrChild(sharedTestsParent, target)) return true;
        var relative = Path.GetRelativePath(temp, target);
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        const string releasePrefix = "speedmeter_rc_drill_";
        return first.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(first[releasePrefix.Length..], "N", out _);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static bool SameOrChild(string parent, string candidate)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RejectReparseComponents(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new InvalidOperationException("Path has no root.");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A reparse point blocks recursive deletion.");
        }
    }

    private static void RejectReparseTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A reparse point blocks recursive deletion.");
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("A reparse point blocks recursive deletion.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
            }
        }
    }

    private static void RegisterUninstallKey(string targetDir, string targetExe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro")
            ?? throw new InvalidOperationException("Windows uninstall registration could not be created.");

        key.SetValue("DisplayName", "SpeedMeter Pro", RegistryValueKind.String);
        key.SetValue("DisplayVersion", ProductIdentity.ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", ProductIdentity.Company, RegistryValueKind.String);
        key.SetValue("InstallLocation", targetDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", $"{targetExe},0", RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{Path.Combine(targetDir, "Uninstall.exe")}\" -Uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{Path.Combine(targetDir, "Uninstall.exe")}\" -Uninstall -Silent", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        var dir = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(workingDirectory);
        link.SetDescription(description);
        link.SetIconLocation(targetPath, 0);

        var file = (IPersistFile)link;
        file.Save(shortcutPath, false);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
