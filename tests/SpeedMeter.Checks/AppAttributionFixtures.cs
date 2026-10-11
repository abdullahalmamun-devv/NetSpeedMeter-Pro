using System;
using SpeedMeter.Windows;

namespace SpeedMeter.Checks;

public static class AppAttributionFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run(Action<string, Action> check)
    {
        check("APPATTR: Supplied PID and creation timestamps distinguish process identities", () =>
        {
            var t0 = new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
            var t1 = new DateTimeOffset(2026, 10, 11, 4, 5, 0, TimeSpan.Zero);

            var proc1 = new ProcessIdentity(1234, t0, "chrome", @"C:\Program Files\Google\Chrome\chrome.exe");

            // Process 1 exits, process 2 is assigned recycled PID 1234 at t1
            Require(AppAttributionSpike.IsPidRecycled(proc1, 1234, t1), "Must detect recycled PID due to distinct StartTime");
            Require(!AppAttributionSpike.IsPidRecycled(proc1, 1234, t0), "Same PID and StartTime is the same process instance");
            Require(!AppAttributionSpike.IsPidRecycled(proc1, 5678, t1), "Different PID is not recycled PID 1234");
        });

        check("APPATTR: Process and application identity records are separate (no mapping provider)", () =>
        {
            var t0 = DateTimeOffset.UtcNow;
            var procA = new ProcessIdentity(1001, t0, "chrome", @"C:\Program Files\Google\Chrome\chrome.exe");
            var procB = new ProcessIdentity(1002, t0, "chrome", @"C:\Program Files\Google\Chrome\chrome.exe");

            var app = new SpeedMeter.Windows.ApplicationIdentity("Google.Chrome", "Google Chrome", "", false, false);

            Require(procA != procB, "Processes must have distinct identity");
            // This checks record structure only; it does not establish an implemented process-to-app mapper.
            Require(app.DisplayName == "Google Chrome", "Application identity fields preserved");
        });

        check("APPATTR: Provider completeness remains NotMeasured for the current token", () =>
        {
            var capability = AppAttributionSpike.DetermineProviderCapability();
            // Audit WS-07: the old test inferred capability from the same role predicate as production.
            Require(capability == AttributionCompleteness.NotMeasured,
                "Without an implemented provider, neither token privilege nor record fixtures establish completeness.");
        });
    }
}
