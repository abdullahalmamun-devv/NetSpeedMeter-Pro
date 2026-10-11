using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace SpeedMeter.App;

public enum SingleInstanceActivationResult
{
    OwnerAcquired,
    ActivatedExistingInSameSession,
    ExistingInDifferentSession,
    ExistingWindowNotFound
}

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const int SwRestore = 9;
    private readonly string userSid;
    private readonly string? customInstanceKey;
    private Mutex? mutex;
    private FileStream? lockFileStream;
    private bool isOwner;
    private bool disposed;

    public string UserSid => userSid;
    public bool IsOwner => isOwner;

    public SingleInstanceCoordinator(string? customInstanceKey = null)
    {
        this.customInstanceKey = customInstanceKey;
        this.userSid = GetEffectiveUserSid();
    }

    public static string GetEffectiveUserSid()
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

    public bool TryAcquireOwnership(out SingleInstanceActivationResult result)
    {
        if (disposed) throw new ObjectDisposedException(nameof(SingleInstanceCoordinator));

        var key = customInstanceKey ?? userSid;
        var globalMutexName = @"Global\SpeedMeterPro.V1." + key;
        var localMutexName = @"Local\SpeedMeterPro.V1." + key;

        bool createdNew = false;
        try
        {
            mutex = new Mutex(true, globalMutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // In restricted environments where Global namespace is restricted for standard users
            try
            {
                mutex = new Mutex(true, localMutexName, out createdNew);
            }
            catch
            {
                createdNew = false;
            }
        }
        catch
        {
            createdNew = false;
        }

        if (!createdNew)
        {
            isOwner = false;
            result = TryActivateExistingWindow();
            return false;
        }

        // Second layer: per-user canonical data store lock file
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedMeterPro");
            Directory.CreateDirectory(dataDir);
            var lockFilePath = Path.Combine(dataDir, $"speedmeter.owner.{key}.lock");
            lockFileStream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Another process holds the lock file for this canonical user store
            mutex?.Dispose();
            mutex = null;
            isOwner = false;
            result = TryActivateExistingWindow();
            return false;
        }

        isOwner = true;
        result = SingleInstanceActivationResult.OwnerAcquired;
        return true;
    }

    private static SingleInstanceActivationResult TryActivateExistingWindow()
    {
        if (!OperatingSystem.IsWindows()) return SingleInstanceActivationResult.ExistingWindowNotFound;

        try
        {
            var hWnd = FindWindow(null, "SpeedMeter Pro");
            if (hWnd != nint.Zero)
            {
                GetWindowThreadProcessId(hWnd, out var processId);
                var sameSession = true;
                try
                {
                    var existingProcess = Process.GetProcessById((int)processId);
                    sameSession = (existingProcess.SessionId == Process.GetCurrentProcess().SessionId);
                }
                catch { }

                if (sameSession)
                {
                    ShowWindow(hWnd, SwRestore);
                    SetForegroundWindow(hWnd);
                    return SingleInstanceActivationResult.ActivatedExistingInSameSession;
                }
                else
                {
                    return SingleInstanceActivationResult.ExistingInDifferentSession;
                }
            }
        }
        catch { }

        return SingleInstanceActivationResult.ExistingWindowNotFound;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            lockFileStream?.Dispose();
            lockFileStream = null;
        }
        catch { }

        try
        {
            if (mutex is not null && isOwner)
            {
                mutex.ReleaseMutex();
            }
            mutex?.Dispose();
            mutex = null;
        }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
