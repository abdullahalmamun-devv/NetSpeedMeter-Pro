using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SpeedMeter.Core;

namespace SpeedMeter.Installer;

// The installed executable cannot remove its own directory. This private handoff
// runs the same signed executable from a unique directory outside that tree.
internal static class UninstallBootstrap
{
    private const string RequestName = "request.json";
    private const string HelperName = "Uninstall.exe";
    private const string MarkerName = ".speedmeter-pro-install-root";
    private const string Mode = "-CleanupExecutor";
    private const string JanitorName = "SpeedMeterPro.TempCleanup.exe";
    private const string JanitorRequestName = "janitor.req";
    private static readonly string HelperParent = Path.Combine(Path.GetTempPath(), "SpeedMeterPro.Cleanup");
    private static string? _currentNonce;
    private static bool _currentSilent;

    private sealed record CleanupRequest(
        string Target, string SourceExecutable, string SourceSha256,
        int ParentPid, long ParentStartUtcTicks, string Nonce, string[] OriginalArgs);

    private sealed record CleanupResult(string TargetHash, string Nonce, string State, string Message, DateTimeOffset Timestamp);

    internal static bool IsMode(string[] args) => (args.Length == 2 ||
        (args.Length == 3 && string.Equals(args[2], "-Silent", StringComparison.OrdinalIgnoreCase))) &&
        string.Equals(args[0], Mode, StringComparison.Ordinal);

    internal static void LogHandoffFailure(string message)
    {
        try
        {
            Directory.CreateDirectory(HelperParent);
            RejectReparsePath(HelperParent);
            var path = Path.Combine(HelperParent, "cleanup.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static string TargetHash(string target) => Convert.ToHexString(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)).ToUpperInvariant())));

    internal static string ResultPath(string target) => Path.Combine(HelperParent, "result-" + TargetHash(target) + ".json");

    internal static bool WriteResult(string target, bool success, string message)
    {
        if (_currentNonce is null) return false;
        WriteResultFile(target, _currentNonce, success ? "ProductRemoved" : "Failed", message);
        return TryStartJanitor(_currentNonce, target, success);
    }

    private static void WriteResultFile(string target, string nonce, string state, string message)
    {
        Directory.CreateDirectory(HelperParent);
        RejectReparsePath(HelperParent);
        var path = ResultPath(target);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new CleanupResult(TargetHash(target), nonce, state, message,
            DateTimeOffset.UtcNow)));
        File.Move(temporary, path, overwrite: true);
    }

    internal static bool TryStart(string[] args, string target)
    {
        var source = Environment.ProcessPath;
        if (source is null || !string.Equals(Path.GetFileName(source), HelperName, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(Path.GetDirectoryName(source)!, target)) return false;

        InstallerEngine.ValidateUninstallRequest(new UninstallOptions(target));
        RequireOwnedMarker(target);
        Directory.CreateDirectory(HelperParent);
        RejectReparsePath(HelperParent);
        if (SameOrChild(target, HelperParent))
            throw new InvalidOperationException("Cleanup helper location must be outside the installation.");
        PruneStaleHelpers();
        EnsureJanitor();
        var nonce = Guid.NewGuid().ToString("N");
        var helperDir = Path.Combine(HelperParent, nonce);
        Directory.CreateDirectory(helperDir);
        var helper = Path.Combine(helperDir, HelperName);
        File.Copy(source, helper, overwrite: false);
        var sourceHash = Hash(source);
        if (!string.Equals(Hash(helper), sourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup helper identity verification failed.");
        using var parent = Process.GetCurrentProcess();
        var request = new CleanupRequest(target, source, sourceHash,
            parent.Id, parent.StartTime.ToUniversalTime().Ticks, nonce, args);
        WriteResultFile(target, nonce, "Pending", "Waiting for the external cleanup helper.");
        var requestPath = Path.Combine(helperDir, RequestName);
        using (var stream = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, request);
        using var server = new NamedPipeServerStream("SpeedMeterPro.Cleanup." + nonce, PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = helperDir };
        start.ArgumentList.Add(Mode);
        start.ArgumentList.Add(requestPath);
        if (args.Any(a => a.Equals("-Silent", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("/qn", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("-qn", StringComparison.OrdinalIgnoreCase)))
            start.ArgumentList.Add("-Silent");
        using var child = Process.Start(start);
        if (child is null)
            throw new InvalidOperationException("Cleanup helper did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        server.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid) || clientPid != child.Id ||
            server.ReadByte() != 0x53)
            throw new InvalidOperationException("Cleanup helper handshake failed.");
        server.WriteByte(0x4D);
        server.Flush();
        return true;
    }

    internal static (string Target, string[] Args) ValidateAndWait(string requestPath)
    {
        var request = ValidateRequest(requestPath);
        WaitForParent(request);

        // The decisive check is after the installed parent has stopped.
        request = ValidateRequest(requestPath);
        InstallerEngine.ValidateUninstallRequest(new UninstallOptions(request.Target));
        RequireOwnedMarker(request.Target);
        _currentNonce = request.Nonce;
        _currentSilent = request.OriginalArgs.Any(a => a.Equals("-Silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/qn", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-qn", StringComparison.OrdinalIgnoreCase));
        try { File.Delete(requestPath); } catch { }
        return (request.Target, request.OriginalArgs);
    }

    private static CleanupRequest ValidateRequest(string requestPath)
        => ValidateRequestCore(requestPath, HelperParent, Environment.ProcessPath ?? "", null);

    internal static void ValidateRequestForTests(string requestPath, string sandboxRoot, string helperExecutable,
        string expectedTarget)
    {
        var root = TestSandboxGuard.ValidateRoot(sandboxRoot);
        TestSandboxGuard.ValidateChild(requestPath, root);
        TestSandboxGuard.ValidateChild(helperExecutable, root);
        TestSandboxGuard.ValidateChild(expectedTarget, root);
        ValidateRequestCore(requestPath, Path.GetDirectoryName(Path.GetDirectoryName(requestPath)!)!,
            helperExecutable, expectedTarget);
    }

    internal static void ValidateParentForTests(string requestPath, string sandboxRoot, string helperExecutable,
        string expectedTarget)
    {
        var root = TestSandboxGuard.ValidateRoot(sandboxRoot);
        TestSandboxGuard.ValidateChild(requestPath, root);
        TestSandboxGuard.ValidateChild(helperExecutable, root);
        TestSandboxGuard.ValidateChild(expectedTarget, root);
        var request = ValidateRequestCore(requestPath,
            Path.GetDirectoryName(Path.GetDirectoryName(requestPath)!)!, helperExecutable, expectedTarget);
        using var parent = Process.GetProcessById(request.ParentPid);
        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks)
            throw new InvalidOperationException("Cleanup parent identity changed.");
    }

    private static void WaitForParent(CleanupRequest request)
    {
        using var parent = Process.GetProcessById(request.ParentPid);
        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks)
            throw new InvalidOperationException("Cleanup parent identity changed.");
        using var client = new NamedPipeClientStream(".", "SpeedMeterPro.Cleanup." + request.Nonce,
            PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        client.Connect(10000);
        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverPid) || serverPid != request.ParentPid)
            throw new InvalidOperationException("Cleanup parent handshake identity changed.");
        client.WriteByte(0x53);
        client.Flush();
        if (client.ReadByte() != 0x4D)
            throw new InvalidOperationException("Cleanup parent handshake failed.");
        client.Dispose();
        if (!parent.WaitForExit(30000))
            throw new InvalidOperationException("Original uninstaller did not exit in time.");
    }

    private static CleanupRequest ValidateRequestCore(string requestPath, string expectedHelperParent,
        string runningHelper, string? expectedTarget)
    {
        var full = Path.GetFullPath(requestPath);
        var dir = Path.GetDirectoryName(full)!;
        var nonce = Path.GetFileName(dir);
        if (!Guid.TryParseExact(nonce, "N", out _) ||
            !SamePath(Path.GetDirectoryName(dir)!, expectedHelperParent) ||
            !string.Equals(Path.GetFileName(full), RequestName, StringComparison.Ordinal))
            throw new InvalidOperationException("Cleanup request location is invalid.");
        RejectReparsePath(full);
        var helper = Path.Combine(dir, HelperName);
        if (!SamePath(runningHelper, helper))
            throw new InvalidOperationException("Cleanup helper was not launched from its verified location.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None);
        if (stream.Length > 16 * 1024)
            throw new InvalidOperationException("Cleanup request is too large.");
        var request = JsonSerializer.Deserialize<CleanupRequest>(stream)
            ?? throw new InvalidOperationException("Cleanup request is empty.");
        if (request.Nonce != nonce || request.ParentPid <= 0 || request.ParentStartUtcTicks <= 0 ||
            request.OriginalArgs is null || request.OriginalArgs.Length > 32 ||
            !string.Equals(Path.GetFileName(request.SourceExecutable), HelperName, StringComparison.OrdinalIgnoreCase) ||
            !SamePath(Path.GetDirectoryName(request.SourceExecutable)!, request.Target) ||
            (expectedTarget is not null && !SamePath(request.Target, expectedTarget)) ||
            !string.Equals(Hash(helper), request.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hash(request.SourceExecutable), request.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            (TargetArgument(request.OriginalArgs) is string specified && !SamePath(specified, request.Target)) ||
            !request.OriginalArgs.Any(a => a.Equals("-Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("/UNINSTALL", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("-u", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("/u", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Cleanup request identity is invalid.");
        InstallerEngine.ValidateUninstallRequest(new UninstallOptions(request.Target));
        RequireOwnedMarker(request.Target);
        return request;
    }

    private static void RequireOwnedMarker(string target)
    {
        var marker = Path.Combine(target, MarkerName);
        if (!File.Exists(marker) ||
            !string.Equals(File.ReadAllText(marker), InstallerEngine.InstallMarkerContent(target), StringComparison.Ordinal))
            throw new InvalidOperationException("The installation ownership marker is invalid.");
    }

    private static string? TargetArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("-Dir", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("/DIR", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-TargetDir", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
                throw new InvalidOperationException("A reparse point blocks cleanup handoff.");
        }
    }

    private static void EnsureJanitor()
    {
        using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("temp-cleanup.exe")
            ?? throw new InvalidOperationException("The signed temporary cleanup component is missing.");
        var expectedHash = Convert.ToHexString(SHA256.HashData(embedded));
        embedded.Position = 0;
        var installed = Path.Combine(HelperParent, JanitorName);
        RejectReparsePath(installed);
        if (File.Exists(installed) && string.Equals(Hash(installed), expectedHash, StringComparison.Ordinal)) return;
        var staging = installed + ".new." + Guid.NewGuid().ToString("N");
        using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            embedded.CopyTo(output);
        if (!string.Equals(Hash(staging), expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Temporary cleanup component integrity check failed.");
        File.Move(staging, installed, overwrite: true);
        if (!string.Equals(Hash(installed), expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Temporary cleanup component changed after extraction.");
    }

    private static bool TryStartJanitor(string nonce, string target, bool productRemoved)
    {
        try
        {
            var dir = Path.Combine(HelperParent, nonce);
            var current = Environment.ProcessPath
                ?? throw new InvalidOperationException("Temporary setup identity is unavailable.");
            if (!Guid.TryParseExact(nonce, "N", out _) ||
                !SamePath(Path.GetDirectoryName(current)!, dir) ||
                !string.Equals(Path.GetFileName(current), HelperName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary setup location changed.");
            RejectReparsePath(current);
            var janitor = Path.Combine(HelperParent, JanitorName);
            RejectReparsePath(janitor);
            using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("temp-cleanup.exe")
                ?? throw new InvalidOperationException("Temporary cleanup component is missing.");
            if (!string.Equals(Hash(janitor), Convert.ToHexString(SHA256.HashData(embedded)), StringComparison.Ordinal))
                throw new InvalidOperationException("Temporary cleanup component identity changed.");
            using var parent = Process.GetCurrentProcess();
            var request = Path.Combine(dir, JanitorRequestName);
            RejectReparsePath(dir);
            using (var stream = new FileStream(request, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("SpeedMeterPro.TempCleanup.v1");
                writer.WriteLine(nonce);
                writer.WriteLine(parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteLine(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteLine(Hash(current));
                writer.WriteLine(TargetHash(target));
                writer.WriteLine(productRemoved ? "ProductRemoved" : "Failed");
                writer.WriteLine(_currentSilent ? "Silent" : "Interactive");
            }
            WriteJanitorResult(nonce, "Pending", "Waiting for temporary setup to exit.");
            using var pipe = new NamedPipeServerStream("SpeedMeterPro.TempCleanup." + nonce, PipeDirection.InOut,
                1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(janitor) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = HelperParent };
            start.ArgumentList.Add(request);
            using var child = Process.Start(start)
                ?? throw new InvalidOperationException("Temporary cleanup component did not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) ||
                clientPid != child.Id || pipe.ReadByte() != 0x53)
                throw new InvalidOperationException("Temporary cleanup handshake failed.");
            pipe.WriteByte(0x4D);
            pipe.Flush();
            return true;
        }
        catch (Exception ex)
        {
            LogHandoffFailure("Temporary setup cleanup could not start: " + ex.Message);
            if (productRemoved)
                try { WriteResultFile(target, nonce, "Failed", "Product was removed, but the temporary setup copy may remain."); }
                catch { }
            try { WriteJanitorResult(nonce, "Failed", ex.Message); } catch { }
            if (productRemoved && !_currentSilent)
                System.Windows.MessageBox.Show(
                    "SpeedMeter Pro was removed, but its temporary setup copy could not be cleaned up. Check the setup cleanup log in your Temp folder.",
                    "SpeedMeter Pro cleanup incomplete", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            return false;
        }
    }

    private static void WriteJanitorResult(string nonce, string state, string message)
    {
        var path = Path.Combine(HelperParent, "janitor-result-" + nonce + ".txt");
        var staging = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllLines(staging, [state, message]);
        File.Move(staging, path, overwrite: true);
    }

    private static void PruneStaleHelpers()
    {
        foreach (var dir in Directory.EnumerateDirectories(HelperParent))
            try
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) >= TimeSpan.FromMinutes(10))
                    TryRemoveHelperCopy(dir);
            }
            catch { /* A live or hostile directory is retained. */ }
        foreach (var file in Directory.EnumerateFiles(HelperParent, "result-*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (name.Length == 76 && name.StartsWith("result-", StringComparison.Ordinal) &&
                    name.EndsWith(".json", StringComparison.Ordinal) &&
                    name.AsSpan(7, 64).ToString().All(Uri.IsHexDigit) &&
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= TimeSpan.FromDays(30))
                    File.Delete(file);
            }
            catch { /* Leave a changed or locked file alone. */ }
        }
        foreach (var file in Directory.EnumerateFiles(HelperParent, "janitor-result-*.txt", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (name.Length == 51 && name.StartsWith("janitor-result-", StringComparison.Ordinal) &&
                    name.EndsWith(".txt", StringComparison.Ordinal) &&
                    Guid.TryParseExact(name.Substring(15, 32), "N", out _) &&
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= TimeSpan.FromDays(30))
                    File.Delete(file);
            }
            catch { }
        }
    }

    private static void TryRemoveHelperCopy(string dir)
    {
        if (!SamePath(Path.GetDirectoryName(dir)!, HelperParent) ||
            !Guid.TryParseExact(Path.GetFileName(dir), "N", out _)) return;
        RejectReparsePath(dir);
        var entries = Directory.GetFileSystemEntries(dir);
        if (entries.Any(entry =>
            (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0 ||
            (!string.Equals(Path.GetFileName(entry), RequestName, StringComparison.Ordinal) &&
             !string.Equals(Path.GetFileName(entry), JanitorRequestName, StringComparison.Ordinal) &&
             !string.Equals(Path.GetFileName(entry), HelperName, StringComparison.OrdinalIgnoreCase)))) return;
        var helper = Path.Combine(dir, HelperName);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HelperName)))
        {
            try { if (SamePath(process.MainModule?.FileName ?? "", helper)) return; }
            catch { return; }
            finally { process.Dispose(); }
        }
        var request = Path.Combine(dir, RequestName);
        if (File.Exists(request)) File.Delete(request);
        var janitorRequest = Path.Combine(dir, JanitorRequestName);
        if (File.Exists(janitorRequest)) File.Delete(janitorRequest);
        if (File.Exists(helper)) File.Delete(helper);
        Directory.Delete(dir, recursive: false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int serverProcessId);

}
