using System;
using System.IO;
using System.Windows;

namespace SpeedMeter.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var keyIndex = Array.IndexOf(e.Args, "--instance-key");
        var customKey = keyIndex >= 0 && e.Args.Length > keyIndex + 1 ? e.Args[keyIndex + 1] : null;

        coordinator = new SingleInstanceCoordinator(customKey);
        if (!coordinator.TryAcquireOwnership(out _))
        {
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
        coordinator?.Dispose();
        coordinator = null;
        base.OnExit(e);
    }
}
