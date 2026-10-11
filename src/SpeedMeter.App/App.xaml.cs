using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace SpeedMeter.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? coordinator;
    private EventWaitHandle? shutdownEvent;
    private RegisteredWaitHandle? registeredWaitHandle;

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

        // Listen for graceful shutdown signals from installers/updaters
        try
        {
            var eventName = @"Local\SpeedMeterPro.Shutdown." + (customKey ?? coordinator.UserSid);
            shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(shutdownEvent, (state, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.Invoke(() => (MainWindow as MainWindow)?.Quit());
                }
            }, null, -1, false);
        }
        catch { }

        SessionEnding += OnSessionEnding;

        var smokeIndex = Array.IndexOf(e.Args, "--smoke");
        var smokeDirectory = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? Path.GetFullPath(e.Args[smokeIndex + 1]) : null;
        var window = new MainWindow(smokeDirectory);
        MainWindow = window;
        window.Show();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Graceful session shutdown on logoff or Windows shutdown
        (MainWindow as MainWindow)?.Quit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            registeredWaitHandle?.Unregister(null);
            shutdownEvent?.Dispose();
            shutdownEvent = null;
        }
        catch { }

        coordinator?.Dispose();
        coordinator = null;
        base.OnExit(e);
    }
}
