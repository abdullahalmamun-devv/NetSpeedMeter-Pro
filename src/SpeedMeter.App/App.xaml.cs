using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SpeedMeter.Application;

namespace SpeedMeter.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? coordinator;
    private EventWaitHandle? shutdownEvent;
    private RegisteredWaitHandle? registeredWaitHandle;
    private CrashReportService? crashReports;
    private OperationalLogger? operationalLogger;
    private bool osSessionEnding;

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

        crashReports = new CrashReportService();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        CrashRecoveryInfo? recovery = null;
        try
        {
            recovery = crashReports.BeginRun();
            operationalLogger = new OperationalLogger();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

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
        try
        {
            var window = new MainWindow(smokeDirectory);
            MainWindow = window;
            window.Show();
            var priorReport = recovery?.Outcome == PreviousRunOutcome.KnownCrash ? recovery.Report : null;
            if (priorReport is not null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (!window.IsLoaded) return;
                    new CrashRecoveryWindow(priorReport) { Owner = window }.Show();
                }));
            }
        }
        catch (Exception ex)
        {
            crashReports?.CaptureFatal(ex, "startup");
            throw;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        crashReports?.CaptureFatal(e.Exception, "WPF Dispatcher");
        // Preserve normal fatal behavior. A diagnostic record is not exception recovery.
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        crashReports?.CaptureFatal(e.ExceptionObject as Exception, "AppDomain");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // An unobserved task is not proof the process crashed. Do not mark this run fatal.
        operationalLogger?.LogWarning("An unobserved background task exception occurred: " + e.Exception.GetType().Name);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Graceful session shutdown on logoff or Windows shutdown
        osSessionEnding = true;
        (MainWindow as MainWindow)?.Quit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        crashReports?.MarkExit(osSessionEnding);
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
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
