using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace SpeedMeter.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\SpeedMeterPro.P0." + Environment.UserName, out var created);
        if (!created)
        {
            MessageBox.Show("SpeedMeter Pro is already running. Open it from the notification area.", "SpeedMeter Pro");
            Shutdown();
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
