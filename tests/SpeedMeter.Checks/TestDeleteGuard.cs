using System;
using System.IO;
using Microsoft.Data.Sqlite;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

/// <summary>Deletes only Core-validated disposable fixture roots and their children.</summary>
internal static class TestDeleteGuard
{
    internal static string CreateRoot() => TestSandboxGuard.CreateRoot();

    internal static void RequireChild(string root, string child) =>
        TestSandboxGuard.ValidateTreeForDeletion(child, root);

    internal static void DeleteRoot(string root)
    {
        // The storage service can return disposed SQLite connections to the process pool.
        // Release those handles before deleting a verified disposable fixture tree.
        SqliteConnection.ClearAllPools();
        var safeRoot = TestSandboxGuard.ValidateRoot(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(safeRoot))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Disposable fixture root contains a reparse point.");
            if (Directory.Exists(entry)) TestSandboxGuard.ValidateTreeForDeletion(entry, safeRoot);
            else TestSandboxGuard.ValidateChild(entry, safeRoot);
        }
        Directory.Delete(safeRoot, recursive: true);
    }
}
