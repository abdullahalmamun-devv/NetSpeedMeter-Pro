using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SpeedMeter.Core;

namespace SpeedMeter.Installer;

public sealed class SpeedMeterRunningException(string message) : InvalidOperationException(message);

public sealed record InstallOptions(
    string? TargetDirectory = null,
    string? PayloadZipPath = null,
    Stream? PayloadStream = null,
    bool Silent = false,
    bool LaunchAfterInstall = false,
    bool CreateDesktopShortcut = false,
    Action<string>? Log = null
);

public sealed record UninstallOptions(
    string? InstallDirectory = null,
    bool PurgeUserData = false,
    bool Silent = false,
    Action<string>? Log = null
);

public static class InstallerEngine
{
    private const int WmClose = 0x0010;

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

    public static bool RequestGracefulShutdown(string userSid, TimeSpan timeout, Action<string>? log = null)
    {
        log?.Invoke("Checking for running SpeedMeter Pro instances...");
        var processes = Process.GetProcessesByName("SpeedMeter.App");
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

        // Wait for processes to exit
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var active = Process.GetProcessesByName("SpeedMeter.App");
            if (active.Length == 0)
            {
                log?.Invoke("SpeedMeter Pro exited gracefully.");
                return true;
            }
            Thread.Sleep(250);
        }

        log?.Invoke("SpeedMeter Pro did not exit within graceful shutdown timeout.");
        return false;
    }

    public static async Task InstallAsync(InstallOptions options)
    {
        var log = options.Log ?? (_ => { });
        var targetDir = options.TargetDirectory ?? GetDefaultInstallDirectory();
        var userSid = GetCurrentUserSid();

        log($"Beginning installation of SpeedMeter Pro {ProductIdentity.ProductVersion}...");
        log($"Target Directory: {targetDir}");

        // 1. Graceful Shutdown Handshake
        var runningProcs = Process.GetProcessesByName("SpeedMeter.App");
        if (runningProcs.Length > 0)
        {
            var closed = RequestGracefulShutdown(userSid, TimeSpan.FromSeconds(5), log);
            if (!closed)
            {
                throw new SpeedMeterRunningException("SpeedMeter Pro is currently running and could not be closed gracefully. Installation stopped to prevent data corruption.");
            }
        }

        // 2. Prepare Directories and Staging
        Directory.CreateDirectory(targetDir);
        var stagingDir = Path.Combine(targetDir, $".staging_{Guid.NewGuid():N}");
        var backupDir = Path.Combine(targetDir, $".backup_{Guid.NewGuid():N}");
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
            var isUpgrade = existingFiles.Length > 0;
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
                File.Move(file, Path.Combine(targetDir, name), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(stagingDir))
            {
                var name = Path.GetFileName(dir);
                var destSubDir = Path.Combine(targetDir, name);
                if (Directory.Exists(destSubDir)) Directory.Delete(destSubDir, true);
                Directory.Move(dir, destSubDir);
            }

            // Copy self as Uninstall.exe into target
            try
            {
                var currentExe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe))
                {
                    var uninstallExePath = Path.Combine(targetDir, "Uninstall.exe");
                    File.Copy(currentExe, uninstallExePath, overwrite: true);
                }
            }
            catch { }

            // 6. Shell Integration (Start Menu shortcut and Registry Uninstall)
            log("Registering Start Menu shortcut and Windows uninstall entries...");
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            var shortcutPath = Path.Combine(startMenuDir, "SpeedMeter Pro.lnk");
            var targetExe = Path.Combine(targetDir, "SpeedMeter.App.exe");
            CreateShortcut(shortcutPath, targetExe, targetDir, "SpeedMeter Pro - Precision Network Performance Monitor");

            if (options.CreateDesktopShortcut)
            {
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var desktopShortcut = Path.Combine(desktopDir, "SpeedMeter Pro.lnk");
                CreateShortcut(desktopShortcut, targetExe, targetDir, "SpeedMeter Pro");
            }

            RegisterUninstallKey(targetDir, targetExe);

            // Clean up staging & backup on success
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
            try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }

            log("Installation completed successfully!");

            if (options.LaunchAfterInstall)
            {
                log("Launching SpeedMeter Pro...");
                Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            log($"Installation failed: {ex.Message}. Initiating transactional rollback...");
            // Rollback Phase
            try
            {
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
            }
            catch (Exception rollbackEx)
            {
                log($"Rollback error: {rollbackEx.Message}");
            }
            finally
            {
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }
            }

            throw;
        }
    }

    public static async Task UninstallAsync(UninstallOptions options)
    {
        var log = options.Log ?? (_ => { });
        var targetDir = options.InstallDirectory ?? GetDefaultInstallDirectory();
        var userDataDir = GetDefaultUserDataDirectory();
        var userSid = GetCurrentUserSid();

        log("Beginning uninstallation of SpeedMeter Pro...");

        // 1. Graceful shutdown check
        var runningProcs = Process.GetProcessesByName("SpeedMeter.App");
        if (runningProcs.Length > 0)
        {
            var closed = RequestGracefulShutdown(userSid, TimeSpan.FromSeconds(5), log);
            if (!closed)
            {
                throw new SpeedMeterRunningException("SpeedMeter Pro is running and could not be closed gracefully. Uninstallation aborted.");
            }
        }

        // 2. Remove Shortcuts
        log("Removing application shortcuts...");
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        var startMenuShortcut = Path.Combine(startMenuDir, "SpeedMeter Pro.lnk");
        try { if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut); } catch { }

        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedMeter Pro.lnk");
        try { if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut); } catch { }

        // 3. Remove Registry Entries
        log("Removing Windows Uninstall and Startup registry entries...");
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

        // 4. User Data Directory Retention vs Purge
        if (options.PurgeUserData)
        {
            log($"Purging all user data and historical databases at {userDataDir}...");
            try
            {
                if (Directory.Exists(userDataDir)) Directory.Delete(userDataDir, recursive: true);
            }
            catch (Exception ex)
            {
                log($"Warning: Could not completely delete user data directory: {ex.Message}");
            }
        }
        else
        {
            log($"Preserving historical database and settings at: {userDataDir}");
        }

        // 5. Remove Program Binaries
        log("Removing application program files...");
        if (Directory.Exists(targetDir))
        {
            var cleanupBat = Path.Combine(Path.GetTempPath(), $"speedmeter_uninstall_{Guid.NewGuid():N}.bat");
            var batContent = $"@echo off\r\nping 127.0.0.1 -n 2 > nul\r\nrd /s /q \"{targetDir}\"\r\ndel \"%~f0\"";
            await File.WriteAllTextAsync(cleanupBat, batContent);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanupBat}\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        log("SpeedMeter Pro has been uninstalled.");
    }

    private static void RegisterUninstallKey(string targetDir, string targetExe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeedMeterPro");
            if (key is null) return;

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
        catch { }
    }

    public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);
            link.SetIconLocation(targetPath, 0);

            var file = (IPersistFile)link;
            file.Save(shortcutPath, false);
        }
        catch
        {
            // Fallback via PowerShell / WScript if COM interface has issue in test context
            try
            {
                var dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetPath}'; $s.WorkingDirectory = '{workingDirectory}'; $s.Description = '{description}'; $s.Save()";
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(psi)?.WaitForExit();
            }
            catch { }
        }
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
