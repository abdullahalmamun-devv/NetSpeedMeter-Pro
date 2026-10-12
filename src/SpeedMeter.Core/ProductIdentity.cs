using System;
using System.Reflection;

namespace SpeedMeter.Core;

public static class ProductIdentity
{
    public const string Name = "SpeedMeter Pro";
    public const string ProductVersion = "1.0.0-rc.2";
    public const string FileVersion = "1.0.0.2";
    public const string AssemblyVersion = "1.0.0.0";
    public const string ReleaseChannel = "rc";
    public const string Company = "SpeedMeter Pro Project";
    public const string Copyright = "Copyright © 2026 SpeedMeter Pro Project";
    public const int DatabaseSchemaVersion = 3;
    public const string TargetArchitecture = "win-x64";

    private static readonly Lazy<string> ResolvedCommitSha = new(ResolveCommitSha);
    private static readonly Lazy<string> ResolvedInformationalVersion = new(ResolveInformationalVersion);

    public static string CommitSha => ResolvedCommitSha.Value;
    public static string InformationalVersion => ResolvedInformationalVersion.Value;

    private static string ResolveInformationalVersion()
    {
        var attr = typeof(ProductIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? $"{ProductVersion}+{CommitSha}";
    }

    private static string ResolveCommitSha()
    {
        var attr = typeof(ProductIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr is not null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
        {
            var parts = attr.InformationalVersion.Split('+');
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return parts[1].Trim();
            }
        }
        return "unspecified";
    }
}
