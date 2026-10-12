using System;
using System.IO;
using System.Reflection;
using System.Windows;
using SpeedMeter.Core;

namespace SpeedMeter.Installer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var isUninstall = HasArg(args, "-Uninstall", "/UNINSTALL", "-u", "/u");
        var isSilent = HasArg(args, "-Silent", "/SILENT", "-s", "/s", "-S", "/S", "/qn", "-qn");
        var purgeData = HasArg(args, "-PurgeUserData", "/PURGE", "-purge", "/purge");
        var noLaunch = HasArg(args, "-NoLaunch", "/NOLAUNCH", "-nolaunch", "/nolaunch");
        var launchAfterInstall = HasArg(args, "-LaunchAfterInstall", "/LAUNCHAFTERINSTALL");
        var desktopShortcut = HasArg(args, "-DesktopShortcut", "/DESKTOPSHORTCUT");
        var startWithWindows = HasArg(args, "-StartWithWindows", "/STARTWITHWINDOWS");
        var payloadPath = GetArgValue(args, "-Payload", "/PAYLOAD", "-payload");
        var targetDir = GetArgValue(args, "-Dir", "/DIR", "-TargetDir", "-dir", "/dir");

        void Log(string message)
        {
            if (!isSilent)
            {
                Console.WriteLine(message);
            }
        }

        try
        {
            Stream? payloadStream = null;
            if (!isUninstall)
            {
                // The same embedded or sidecar payload is used by interactive and silent setup.
                var asm = Assembly.GetExecutingAssembly();
                payloadStream = asm.GetManifestResourceStream("payload.zip");
                if (payloadStream is null && string.IsNullOrWhiteSpace(payloadPath))
                {
                    var baseDir = AppContext.BaseDirectory;
                    var sidecar1 = Path.Combine(baseDir, "payload.zip");
                    var sidecar2 = Path.Combine(baseDir, $"SpeedMeterPro-{ProductIdentity.ProductVersion}-win-x64.zip");
                    if (File.Exists(sidecar1)) payloadPath = sidecar1;
                    else if (File.Exists(sidecar2)) payloadPath = sidecar2;
                }
            }

            if (!isSilent)
            {
                var ui = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var window = new InstallerWindow(
                    isUninstall, targetDir, payloadPath, payloadStream,
                    purgeData, noLaunch, launchAfterInstall);
                ui.Run(window);
                return window.ExitCode;
            }

            if (isUninstall)
            {
                var uninstallOptions = new UninstallOptions(
                    InstallDirectory: targetDir,
                    PurgeUserData: purgeData,
                    Silent: isSilent,
                    Log: Log);

                InstallerEngine.UninstallAsync(uninstallOptions).GetAwaiter().GetResult();
                return 0;
            }

            var installOptions = new InstallOptions(
                TargetDirectory: targetDir,
                PayloadZipPath: payloadPath,
                PayloadStream: payloadStream,
                Silent: isSilent,
                LaunchAfterInstall: launchAfterInstall && !noLaunch,
                CreateDesktopShortcut: desktopShortcut ? true : null,
                StartWithWindows: startWithWindows ? true : null,
                Log: Log);

            InstallerEngine.InstallAsync(installOptions).GetAwaiter().GetResult();
            return 0;
        }
        catch (SpeedMeterRunningException ex)
        {
            Log($"[ERROR] {ex.Message}");
            return 1618;
        }
        catch (Exception ex)
        {
            Log($"[FATAL ERROR] {ex.Message}");
            return 1;
        }
    }

    private static bool HasArg(string[] args, params string[] names)
    {
        foreach (var arg in args)
        {
            foreach (var name in names)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static string? GetArgValue(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            foreach (var name in names)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
        }
        return null;
    }
}
