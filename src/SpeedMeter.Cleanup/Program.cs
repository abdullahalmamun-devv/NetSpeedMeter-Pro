using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SpeedMeter.Core;

[assembly: InternalsVisibleTo("SpeedMeter.Checks")]

namespace SpeedMeter.Cleanup;

internal static class Program
{
    private const string ExeName = "SpeedMeterPro.TempCleanup.exe";
    private const string RequestName = "janitor.req";
    private const string SetupName = "Uninstall.exe";
    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "SpeedMeterPro.Cleanup");
    private sealed record CleanupRequest(string Path, string Directory, string Nonce, string Setup,
        string[] Lines, int ParentPid, long ParentTicks);

    private static int Main(string[] args)
    {
        string? nonce = null;
        string? targetHash = null;
        var productRemoved = false;
        var interactive = false;
        try
        {
            if (args.Length != 1) throw new InvalidOperationException("Cleanup request is missing.");
            var request = ValidateRequestCore(args[0], Parent, Environment.ProcessPath ?? "", null);
            var requestPath = request.Path;
            var dir = request.Directory;
            nonce = request.Nonce;
            var setup = request.Setup;
            var lines = request.Lines;
            using var parent = Process.GetProcessById(request.ParentPid);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentTicks ||
                !SamePath(parent.MainModule?.FileName ?? "", setup))
                throw new InvalidOperationException("Cleanup parent identity changed.");
            using (var pipe = new NamedPipeClientStream(".", "SpeedMeterPro.TempCleanup." + nonce,
                PipeDirection.InOut, PipeOptions.CurrentUserOnly))
            {
                pipe.Connect(10000);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != request.ParentPid)
                    throw new InvalidOperationException("Cleanup handshake parent changed.");
                pipe.WriteByte(0x53);
                pipe.Flush();
                if (pipe.ReadByte() != 0x4D)
                    throw new InvalidOperationException("Cleanup handshake failed.");
            }
            targetHash = lines[5];
            productRemoved = lines[6] == "ProductRemoved";
            interactive = lines[7] == "Interactive";
            if (!parent.WaitForExit(30000))
                throw new InvalidOperationException("Cleanup parent did not exit in time.");
            DeleteVerifiedSetup(request, Parent);
            WriteResult(nonce, "Completed", "Temporary setup copy removed.");
            if (productRemoved)
                WriteMainResult(targetHash, nonce, "Completed", "Product and temporary setup copy removed.");
            return 0;
        }
        catch (Exception ex)
        {
            if (nonce is not null && Guid.TryParseExact(nonce, "N", out _))
                try { WriteResult(nonce, "Failed", ex.Message); } catch { }
            if (productRemoved && targetHash is not null && nonce is not null)
                try { WriteMainResult(targetHash, nonce, "Failed",
                    "Product was removed, but a temporary setup copy may remain."); } catch { }
            if (interactive && productRemoved)
                MessageBox(nint.Zero,
                    "SpeedMeter Pro was removed, but a temporary setup copy could not be deleted. Check the setup cleanup log in your Temp folder.",
                    "SpeedMeter Pro cleanup incomplete", 0x30);
            return 1;
        }
    }

    internal static void ValidateRequestForTests(string requestPath, string sandboxRoot,
        string runningJanitorPath, string expectedSetupPath)
    {
        var root = TestSandboxGuard.ValidateRoot(sandboxRoot);
        TestSandboxGuard.ValidateChild(requestPath, root);
        TestSandboxGuard.ValidateChild(runningJanitorPath, root);
        TestSandboxGuard.ValidateChild(expectedSetupPath, root);
        ValidateRequestCore(requestPath, Path.GetDirectoryName(runningJanitorPath)!, runningJanitorPath,
            expectedSetupPath);
    }

    internal static void ValidateParentForTests(string requestPath, string sandboxRoot,
        string runningJanitorPath, string expectedSetupPath)
    {
        var root = TestSandboxGuard.ValidateRoot(sandboxRoot);
        TestSandboxGuard.ValidateChild(requestPath, root);
        TestSandboxGuard.ValidateChild(runningJanitorPath, root);
        TestSandboxGuard.ValidateChild(expectedSetupPath, root);
        var request = ValidateRequestCore(requestPath, Path.GetDirectoryName(runningJanitorPath)!,
            runningJanitorPath, expectedSetupPath);
        using var parent = Process.GetProcessById(request.ParentPid);
        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentTicks ||
            !SamePath(parent.MainModule?.FileName ?? "", request.Setup))
            throw new InvalidOperationException("Cleanup parent identity changed.");
    }

    internal static void DeleteVerifiedSetupForTests(string requestPath, string sandboxRoot,
        string runningJanitorPath, string expectedSetupPath)
    {
        var root = TestSandboxGuard.ValidateRoot(sandboxRoot);
        TestSandboxGuard.ValidateChild(requestPath, root);
        TestSandboxGuard.ValidateChild(runningJanitorPath, root);
        TestSandboxGuard.ValidateChild(expectedSetupPath, root);
        var parent = Path.GetDirectoryName(runningJanitorPath)!;
        var request = ValidateRequestCore(requestPath, parent, runningJanitorPath, expectedSetupPath);
        DeleteVerifiedSetup(request, parent);
    }

    private static void DeleteVerifiedSetup(CleanupRequest request, string expectedParent)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (!SamePath(Path.GetDirectoryName(request.Directory)!, expectedParent))
                throw new InvalidOperationException("Cleanup directory changed.");
            RejectReparsePath(request.Setup);
            if (!string.Equals(Hash(request.Setup), request.Lines[4], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cleanup executable changed before deletion.");
            try { File.Delete(request.Setup); break; }
            catch (IOException) when (attempt < 29) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 29) { Thread.Sleep(100); }
        }
        if (File.Exists(request.Setup)) throw new IOException("Temporary setup copy remains after cleanup.");
        RejectReparsePath(request.Path);
        File.Delete(request.Path);
        RejectReparsePath(request.Directory);
        Directory.Delete(request.Directory, recursive: false);
    }

    private static CleanupRequest ValidateRequestCore(string requestPath, string expectedParent,
        string runningJanitorPath, string? expectedSetupPath)
    {
        var full = Path.GetFullPath(requestPath);
        var dir = Path.GetDirectoryName(full)!;
        var nonce = Path.GetFileName(dir);
        if (!Guid.TryParseExact(nonce, "N", out _) ||
            !SamePath(Path.GetDirectoryName(dir)!, expectedParent) ||
            !string.Equals(Path.GetFileName(full), RequestName, StringComparison.Ordinal) ||
            !SamePath(runningJanitorPath, Path.Combine(expectedParent, ExeName)))
            throw new InvalidOperationException("Cleanup request is outside the product temp directory.");
        RejectReparsePath(full);
        var lines = new string[8];
        using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (stream.Length > 1024)
                throw new InvalidOperationException("Cleanup request is too large.");
            using var reader = new StreamReader(stream);
            for (var i = 0; i < lines.Length; i++)
                lines[i] = reader.ReadLine() ?? throw new InvalidOperationException("Cleanup request is incomplete.");
            if (reader.ReadLine() is not null)
                throw new InvalidOperationException("Cleanup request has extra fields.");
        }
        if (lines[0] != "SpeedMeterPro.TempCleanup.v1" || lines[1] != nonce ||
            !int.TryParse(lines[2], out var parentPid) || parentPid <= 0 ||
            !long.TryParse(lines[3], out var parentTicks) || parentTicks <= 0 ||
            lines[4].Length != 64 || !lines[4].All(Uri.IsHexDigit) ||
            lines[5].Length != 64 || !lines[5].All(Uri.IsHexDigit) ||
            lines[6] is not ("ProductRemoved" or "Failed") ||
            lines[7] is not ("Silent" or "Interactive"))
            throw new InvalidOperationException("Cleanup request content is invalid.");
        var setup = Path.Combine(dir, SetupName);
        if (expectedSetupPath is not null && !SamePath(setup, expectedSetupPath))
            throw new InvalidOperationException("Cleanup setup target changed.");
        RejectReparsePath(setup);
        if (!string.Equals(Hash(setup), lines[4], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup executable identity changed.");
        return new CleanupRequest(full, dir, nonce, setup, lines, parentPid, parentTicks);
    }

    private static void WriteMainResult(string targetHash, string nonce, string state, string message)
    {
        // These fields are validated hex tokens or fixed literal text, so no
        // caller-controlled path or JSON content is serialized here.
        RejectReparsePath(Parent);
        var path = Path.Combine(Parent, "result-" + targetHash + ".json");
        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(temp, "{\"TargetHash\":\"" + targetHash + "\",\"Nonce\":\"" + nonce +
            "\",\"State\":\"" + state + "\",\"Message\":\"" + message +
            "\",\"Timestamp\":\"" + timestamp + "\"}");
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteResult(string nonce, string state, string message)
    {
        Directory.CreateDirectory(Parent);
        RejectReparsePath(Parent);
        var path = Path.Combine(Parent, "janitor-result-" + nonce + ".txt");
        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllLines(temp, [state, message]);
        File.Move(temp, path, overwrite: true);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void RejectReparsePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A reparse point blocks temporary cleanup.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int serverProcessId);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
