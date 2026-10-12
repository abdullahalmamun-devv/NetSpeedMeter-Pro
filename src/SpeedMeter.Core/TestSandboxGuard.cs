using System;
using System.Collections.Generic;
using System.IO;

namespace SpeedMeter.Core;

/// <summary>
/// Fail-closed path validation for disposable integration-test data. This is not
/// an alternate production data directory or a consumer uninstall option.
/// </summary>
public static class TestSandboxGuard
{
    public const string SentinelFileName = ".speedmeter-test-root";
    private const string ParentDirectoryName = "SpeedMeterPro.Tests";
    private const string SentinelPrefix = "SpeedMeterPro.Tests:";
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public static string CreateRoot()
    {
        var parent = GetParent();
        RejectReparseAncestors(parent);
        Directory.CreateDirectory(parent);
        RejectReparseAncestors(parent);
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(parent, id);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SentinelFileName), SentinelPrefix + id);
        return ValidateRoot(root);
    }

    public static string ValidateRoot(string path)
    {
        var full = NormalizeAbsolute(path);
        var expectedParent = GetParent();
        if (!string.Equals(Path.GetDirectoryName(full), expectedParent, PathComparison) ||
            !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new InvalidOperationException("Disposable test root must be a GUID directory under the approved temp parent.");
        RejectReparseAncestors(full);
        var sentinel = Path.Combine(full, SentinelFileName);
        if (!Directory.Exists(full) || !File.Exists(sentinel) ||
            (File.GetAttributes(sentinel) & FileAttributes.ReparsePoint) != 0 ||
            File.ReadAllText(sentinel) != SentinelPrefix + Path.GetFileName(full))
            throw new InvalidOperationException("Disposable test root sentinel is missing or invalid.");
        return full;
    }

    public static string ValidateChild(string path, string root)
    {
        var safeRoot = ValidateRoot(root);
        var full = NormalizeAbsolute(path);
        if (!full.StartsWith(safeRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidOperationException("Destructive test path escaped its disposable test root.");
        RejectReparseAncestors(full);
        return full;
    }

    public static void ValidateTreeForDeletion(string path, string root)
    {
        var full = ValidateChild(path, root);
        if (!Directory.Exists(full)) return;
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Disposable test tree contains a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static string GetParent() =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), ParentDirectoryName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Disposable test path must be absolute.");
        foreach (var segment in path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
            if (segment is "." or "..")
                throw new InvalidOperationException("Disposable test path must not contain dot segments.");
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Disposable test path traverses a reparse point.");
            }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison)) break;
        }
    }
}
