using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SpeedMeter.Core;

namespace SpeedMeter.Installer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var isUninstall = HasArg(args, "-Uninstall", "/UNINSTALL", "-u", "/u");
        var isSilent = HasArg(args, "-Silent", "/SILENT", "-s", "/s", "-S", "/S", "/qn", "-qn");
        var purgeData = HasArg(args, "-PurgeUserData", "/PURGE", "-purge", "/purge");
        var noLaunch = HasArg(args, "-NoLaunch", "/NOLAUNCH", "-nolaunch", "/nolaunch");
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
            if (isUninstall)
            {
                var uninstallOptions = new UninstallOptions(
                    InstallDirectory: targetDir,
                    PurgeUserData: purgeData,
                    Silent: isSilent,
                    Log: Log);

                await InstallerEngine.UninstallAsync(uninstallOptions);
                return 0;
            }

            // Installation mode
            Stream? payloadStream = null;

            // 1. Check embedded resource
            var asm = Assembly.GetExecutingAssembly();
            var resourceStream = asm.GetManifestResourceStream("payload.zip");
            if (resourceStream is not null)
            {
                payloadStream = resourceStream;
            }
            else if (string.IsNullOrWhiteSpace(payloadPath))
            {
                // 2. Check sidecar payload in same directory
                var baseDir = AppContext.BaseDirectory;
                var sidecar1 = Path.Combine(baseDir, "payload.zip");
                var sidecar2 = Path.Combine(baseDir, $"SpeedMeterPro-{ProductIdentity.ProductVersion}-win-x64.zip");
                if (File.Exists(sidecar1)) payloadPath = sidecar1;
                else if (File.Exists(sidecar2)) payloadPath = sidecar2;
            }

            var installOptions = new InstallOptions(
                TargetDirectory: targetDir,
                PayloadZipPath: payloadPath,
                PayloadStream: payloadStream,
                Silent: isSilent,
                LaunchAfterInstall: !noLaunch && !isSilent,
                CreateDesktopShortcut: true,
                Log: Log);

            await InstallerEngine.InstallAsync(installOptions);
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
