using System;
using System.IO;
using System.Linq;
using SpeedMeter.Application;

namespace SpeedMeter.Checks;

internal static class ConsumerCrashFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("CONS-CRASH: Fatal report is local, redacted, bounded and available next launch", FatalReportAndRecovery);
        check("CONS-CRASH: Clean, OS shutdown and unknown unclean exits are distinct", ExitStateDistinctions);
        check("CONS-CRASH: Report retention is capped at twelve files", RetentionCap);
        check("CONS-CRASH: GitHub issue URI includes metadata without diagnostic content", IssueUriPrivacy);
    }

    private static void FatalReportAndRecovery()
    {
        WithDirectory(directory =>
        {
            var first = new CrashReportService(directory);
            Require(first.BeginRun().Outcome == PreviousRunOutcome.None, "First run should have no previous outcome.");
            var exception = new InvalidOperationException("SSID='Private WiFi' token=supersecret IP 192.168.4.8 MAC aa:bb:cc:dd:ee:ff path C:\\Users\\Alice\\notes.txt web https://private.example/visit");
            var report = first.CaptureFatal(exception, "fixture");
            Require(report is not null, "Fatal report was not written.");
            var content = CrashReportService.FormatReport(report!);
            foreach (var secret in new[] { "Private WiFi", "supersecret", "192.168.4.8", "aa:bb:cc:dd:ee:ff", "Alice", "private.example" })
                Require(!content.Contains(secret, StringComparison.OrdinalIgnoreCase), "Sensitive content survived redaction: " + secret);
            Require(content.Contains("[REDACTED", StringComparison.Ordinal), "No redaction markers in preview.");
            first.MarkExit(false);
            var recovery = new CrashReportService(directory).BeginRun();
            Require(recovery.Outcome == PreviousRunOutcome.KnownCrash && recovery.Report?.Id == report!.Id, "Known crash not recovered after restart.");
            Require(new FileInfo(Path.Combine(directory, $"crash-{report!.Id}.json")).Length <= 24 * 1024, "Report exceeded size ceiling.");
        });
    }

    private static void ExitStateDistinctions()
    {
        WithDirectory(directory =>
        {
            var service = new CrashReportService(directory);
            service.BeginRun();
            service.MarkExit(false);
            service = new CrashReportService(directory);
            Require(service.BeginRun().Outcome == PreviousRunOutcome.Clean, "Clean quit was not recognized.");
            service.MarkExit(true);
            service = new CrashReportService(directory);
            Require(service.BeginRun().Outcome == PreviousRunOutcome.OsShutdown, "OS shutdown was not recognized.");
            // Simulate abrupt termination by leaving the run marker in its running state.
            service = new CrashReportService(directory);
            Require(service.BeginRun().Outcome == PreviousRunOutcome.UncleanUnknown, "Abrupt termination was mislabeled.");
        });
    }

    private static void RetentionCap()
    {
        WithDirectory(directory =>
        {
            for (var i = 0; i < 16; i++)
            {
                var service = new CrashReportService(directory);
                service.BeginRun();
                Require(service.CaptureFatal(new Exception("controlled fixture"), "fixture") is not null, "Capture failed.");
            }
            Require(Directory.GetFiles(directory, "crash-*.json").Length == 12, "Report retention is unbounded.");
        });
    }

    private static void IssueUriPrivacy()
    {
        WithDirectory(directory =>
        {
            var service = new CrashReportService(directory);
            service.BeginRun();
            var report = service.CaptureFatal(new Exception("hidden diagnostic secret"), "fixture");
            Require(report is not null, "Capture failed.");
            var url = CrashReportService.CreateIssueUri(report!).AbsoluteUri;
            Require(url.StartsWith("https://github.com/abdullahalmamun-devv/NetSpeedMeter-Pro/issues/new?", StringComparison.Ordinal), "Wrong issue endpoint.");
            Require(url.Contains(report!.Id, StringComparison.Ordinal) && url.Contains("body=", StringComparison.Ordinal), "Expected safe metadata missing.");
            Require(!url.Contains("hidden", StringComparison.OrdinalIgnoreCase) && !url.Contains("StackTrace", StringComparison.Ordinal), "Diagnostic body leaked to URL.");
        });
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeedMeterCrashFixture-" + Guid.NewGuid().ToString("N"));
        try { action(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
