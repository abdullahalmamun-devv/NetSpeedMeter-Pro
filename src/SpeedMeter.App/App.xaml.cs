using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace SpeedMeter.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private const int SwRestore = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\SpeedMeterPro.V1." + Environment.UserName, out var created);
        if (!created)
        {
            try
            {
                var hWnd = FindWindow(null, "SpeedMeter Pro");
                if (hWnd != nint.Zero)
                {
                    ShowWindow(hWnd, SwRestore);
                    SetForegroundWindow(hWnd);
                }
            }
            catch { }

            Shutdown(0);
            return;
        }
        var smokeIndex = Array.IndexOf(e.Args, "--smoke");
        var smokeDirectory = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? Path.GetFullPath(e.Args[smokeIndex + 1]) : null;
        var window = new MainWindow(smokeDirectory);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
