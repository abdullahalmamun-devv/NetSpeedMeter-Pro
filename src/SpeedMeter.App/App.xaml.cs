using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SpeedMeter.Application;
using SpeedMeter.Core;

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
        var smokeIndex = Array.IndexOf(e.Args, "--smoke");
        var smokeDirectory = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? Path.GetFullPath(e.Args[smokeIndex + 1]) : null;
        var testRootIndex = Array.IndexOf(e.Args, "--test-root");
        string? isolatedDataDirectory = null;
        if (smokeIndex >= 0 && smokeDirectory is null)
            throw new InvalidOperationException("UI smoke requires an output directory.");
        if (testRootIndex >= 0 && e.Args.Length <= testRootIndex + 1)
            throw new InvalidOperationException("Test root is missing.");
        if (testRootIndex >= 0 || smokeIndex >= 0)
        {
            // Test data must never fall through to the current user's product store.
            var testRoot = testRootIndex >= 0
                ? TestSandboxGuard.ValidateRoot(e.Args[testRootIndex + 1])
                : TestSandboxGuard.CreateRoot();
            isolatedDataDirectory = TestSandboxGuard.ValidateChild(Path.Combine(testRoot, "data"), testRoot);
            Directory.CreateDirectory(isolatedDataDirectory);
            customKey ??= Path.GetFileName(testRoot);
        }
#if SPEEDMETER_UPDATE_LAB
        // This branch is compiled only into disposable lab binaries. A normal
        // consumer build has no environment, CLI or Settings path to test trust.
        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(installDirectory), "install", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Development update lab app must run from its isolated install directory.");
        var labRoot = TestSandboxGuard.ValidateRoot(Path.GetDirectoryName(installDirectory)!);
        TestSandboxGuard.ValidateChild(installDirectory, labRoot);
        isolatedDataDirectory = TestSandboxGuard.ValidateChild(Path.Combine(labRoot, "data"), labRoot);
        Directory.CreateDirectory(isolatedDataDirectory);
        var labManifestFile = TestSandboxGuard.ValidateChild(
            Path.Combine(labRoot, "update-manifest-url.txt"), labRoot);
        var labManifestUrl = File.ReadAllText(labManifestFile).Trim();
        if (!Uri.TryCreate(labManifestUrl, UriKind.Absolute, out var labManifestUri))
            throw new InvalidOperationException("Development update lab manifest URL is invalid.");
        UpdateService.ConfigureDevelopmentLab(labManifestUri, labRoot,
            Path.Combine(isolatedDataDirectory, "speedmeter.db"));
        customKey = Path.GetFileName(labRoot);
#endif

        coordinator = new SingleInstanceCoordinator(customKey, isolatedDataDirectory);
        if (!coordinator.TryAcquireOwnership(out _))
        {
            Shutdown(0);
            return;
        }

        crashReports = new CrashReportService(isolatedDataDirectory is null
            ? null : Path.Combine(isolatedDataDirectory, "crashes"));
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        CrashRecoveryInfo? recovery = null;
        try
        {
            recovery = crashReports.BeginRun();
            operationalLogger = new OperationalLogger(isolatedDataDirectory is null
                ? null : Path.Combine(isolatedDataDirectory, "logs"));
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

        try
        {
            var window = new MainWindow(smokeDirectory, isolatedDataDirectory);
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
