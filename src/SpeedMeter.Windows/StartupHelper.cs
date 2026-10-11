using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SpeedMeter.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class StartupHelper
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "SpeedMeterPro";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            return key?.GetValue(AppValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartupEnabled(bool enabled, int delaySeconds = 0)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false;

                var command = delaySeconds > 0
                    ? $"\"{exePath}\" --startup-delay {delaySeconds}"
                    : $"\"{exePath}\"";

                key.SetValue(AppValueName, command, RegistryValueKind.String);
                return true;
            }
            else
            {
                if (key.GetValue(AppValueName) is not null)
                {
                    key.DeleteValue(AppValueName, throwOnMissingValue: false);
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
