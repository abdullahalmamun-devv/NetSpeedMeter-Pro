using System.Diagnostics;
using System.Net.NetworkInformation;
using SpeedMeter.Core;
using SpeedMeter.Windows;

if (SpeedMeter.Checks.AuditFixtures.TryRunChild(args) is int childExit) return childExit;
if (SpeedMeter.Checks.V1FoundationFixtures.TryRunChild(args) is int v1ChildExit) return v1ChildExit;

var passed = 0;
var skipped = 0;
var failed = 0;

void Check(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine("PASS " + name);
        passed++;
    }
    catch (SpeedMeter.Checks.SkipCheckException skip)
    {
        Console.WriteLine($"SKIP {name} ({skip.Message})");
        skipped++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
        failed++;
        Environment.ExitCode = 1;
    }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
AdapterCounter Counter(ulong rx, ulong tx = 0, string id = "a", bool up = true) => new(id, "Test adapter", "Fixture", up, rx, tx, 0);
var utc = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
MeasurementEngine Engine() { var e = new MeasurementEngine(); e.SelectScope("a"); return e; }

Check("AC-M01 baseline is unavailable, never fabricated zero", () =>
{
    var r = Engine().Sample(Counter(100), 0, utc, 1);
    Require(r.State == ReadingState.Measuring && r.Download is null && r.Received == 0, "Invalid baseline.");
});
Check("AC-M01 exact bytes and documented 100 Mbps conversion", () =>
{
    var e = Engine(); e.Sample(Counter(100), 0, utc, 1);
    var r = e.Sample(Counter(125000100, 1000), 10, utc.AddSeconds(10), 1);
    Require(r.Received == 125000000 && r.Sent == 1000 && r.Download == 12500000, "Wrong totals or rate.");
    Require(MetricFormat.Rate(r.Download, true) == "100.00 Mbps", "Wrong bit conversion.");
});
Check("AC-M02 delayed tick uses actual elapsed time and preserves bytes", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1);
    var r = e.Sample(Counter(900), 9, utc.AddSeconds(9), 1);
    Require(r.Coarse && r.Download == 100 && r.Received == 900 && r.CoveredSeconds == 9, "Lost delayed bytes.");
});
Check("AC-M03 counter reset does not create a spike", () =>
{
    var e = Engine(); e.Sample(Counter(1000), 0, utc, 1);
    var reset = e.Sample(Counter(5), 1, utc, 1);
    var next = e.Sample(Counter(15), 2, utc, 1);
    Require(reset.State == ReadingState.Discontinuity && reset.Download is null && next.Received == 10, "Reset counted incorrectly.");
});
Check("AC-M04 formatting has no accounting side effects", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1); var r = e.Sample(Counter(123456), 1, utc, 1);
    _ = MetricFormat.Rate(r.Download, true); _ = MetricFormat.Rate(r.Download, false);
    Require(r.Received == 123456 && MetricFormat.Rate(null) == "—" && MetricFormat.Rate(0) == "0 B/s", "Unavailable / idle confused.");
});
Check("AC-M05 pause discards unobserved bytes and preserves prior session", () =>
{
    var e = Engine(); e.Sample(Counter(100), 0, utc, 1); e.Sample(Counter(200), 1, utc, 1);
    e.Interrupt(utc, ReadingState.Paused, "paused");
    var resume = e.Sample(Counter(10000), 100, utc, 1);
    var next = e.Sample(Counter(10050), 101, utc, 1);
    Require(resume.Download is null && next.Received == 150 && next.CoveredSeconds == 2, "Gap was counted.");
});
Check("AC-A02 missing selected adapter never switches silently", () =>
{
    var e = Engine(); e.Sample(Counter(100), 0, utc, 1);
    var r = e.Sample(Counter(10000, id: "other"), 1, utc, 1);
    Require(r.State == ReadingState.Unavailable && r.Received == 0, "Wrong adapter counted.");
});
Check("Disconnected and restored adapter rebaseline", () =>
{
    var e = Engine(); e.Sample(Counter(100), 0, utc, 1);
    e.Sample(Counter(100, up: false), 1, utc, 1);
    var r = e.Sample(Counter(900), 2, utc, 1);
    Require(r.State == ReadingState.Measuring && r.Received == 0, "Reconnect gap counted.");
});
Check("Scope change starts a distinct session", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1); e.Sample(Counter(100), 1, utc, 1);
    e.SelectScope("b"); var r = e.Sample(Counter(999, id: "b"), 2, utc, 1);
    Require(r.Received == 0 && r.Download is null && r.ScopeId == "b", "Scopes mixed.");
});
Check("AC-M08 wall clock reversal never changes rate or bytes", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1);
    var r = e.Sample(Counter(100), 2, utc.AddHours(-1), 1);
    Require(r.Download == 50 && r.Received == 100, "Wall clock used for rate.");
});
Check("Nonpositive monotonic interval is a discontinuity", () =>
{
    var e = Engine(); e.Sample(Counter(0), 10, utc, 1);
    var r = e.Sample(Counter(100), 10, utc, 1);
    Require(r.Download is null && r.Received == 0, "Invalid interval counted.");
});
Check("AC-G03 reset clears session and rebaselines", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1); e.Sample(Counter(100), 1, utc, 1);
    e.ResetSession(); var r = e.Sample(Counter(200), 2, utc, 1);
    Require(r.Received == 0 && r.CoveredSeconds == 0 && r.Download is null, "Reset failed.");
});
Check("Session overflow cannot partially commit directions", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1); e.Sample(Counter(ulong.MaxValue - 1, 2), 1, utc, 1);
    e.Interrupt(utc, ReadingState.Paused, "gap"); e.Sample(Counter(0), 2, utc, 1);
    var r = e.Sample(Counter(10, 10), 3, utc, 1);
    Require(r.State == ReadingState.Discontinuity && r.Received == ulong.MaxValue - 1 && r.Sent == 2, "Overflow mutated totals.");
});
Check("Unknown link speed does not reject plausible high rates", () =>
{
    var e = Engine(); e.Sample(Counter(0), 0, utc, 1); var r = e.Sample(Counter(1250000000), 1, utc, 1);
    Require(r.IsValid && r.Received == 1250000000, "Link speed incorrectly used as a cap.");
});

MeasurementFixtures.Run(Check);
SpeedMeter.Checks.QaFixtures.Run(Check);
SpeedMeter.Checks.ScopeFixtures.Run(Check);
SpeedMeter.Checks.TaskbarFixtures.Run(Check);
SpeedMeter.Checks.AppAttributionFixtures.Run(Check);
SpeedMeter.Checks.AuditFixtures.Run(Check);
SpeedMeter.Checks.WindowsAuditFixtures.Run(Check);
SpeedMeter.Checks.V1FoundationFixtures.Run(Check);
SpeedMeter.Checks.V1Wave2Fixtures.Run(Check);
SpeedMeter.Checks.V1Wave3Fixtures.Run(Check);
SpeedMeter.Checks.V1CoreTruthFixtures.Run(Check);
SpeedMeter.Checks.V1ClockShutdownFixtures.Run(Check);
SpeedMeter.Checks.ConnectivityFixtures.Run(Check);
SpeedMeter.Checks.AlertFixtures.Run(Check);
SpeedMeter.Checks.NetworkToolsFixtures.Run(Check);
SpeedMeter.Checks.ConfigExportImportFixtures.RunAll();

if (args.Contains("--windows"))
{
    Check("Windows native layout and counter parity with managed OS API", () =>
    {
        var provider = new AdapterProvider();
        var before = provider.Read();
        var compared = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(nic.Id, out var id)) continue;
            var first = before.FirstOrDefault(a => a.Id == id.ToString("D"));
            if (first is null) continue;
            var managed = nic.GetIPv4Statistics();
            var after = provider.Read().FirstOrDefault(a => a.Id == first.Id);
            if (after is null || after.Received < first.Received || after.Sent < first.Sent) continue;
            Require((ulong)managed.BytesReceived >= first.Received && (ulong)managed.BytesReceived <= after.Received, "Native received counter mismatch: " + nic.Name);
            Require((ulong)managed.BytesSent >= first.Sent && (ulong)managed.BytesSent <= after.Sent, "Native sent counter mismatch: " + nic.Name);
            Require(first.Name == nic.Name, "Native string layout mismatch.");
            compared++;
        }
        Require(compared > 0, "No interfaces available for comparison.");
        Console.WriteLine($"  Compared {compared} adapter(s); no probes or transfers initiated.");
    });

    Check("Windows IAdapterCounterProvider typed snapshot contract", () =>
    {
        var provider = new AdapterProvider();
        var snapshot = provider.ReadSnapshot();
        Require(snapshot.Availability == ProviderAvailability.Available, "ReadSnapshot availability not Available: " + snapshot.Detail);
        Require(snapshot.Method == "GetIfTable2", "ReadSnapshot method must be GetIfTable2.");
        Require(snapshot.Adapters.Count > 0, "No adapters returned by ReadSnapshot.");
        Console.WriteLine($"  ReadSnapshot observed {snapshot.Adapters.Count} adapter(s) via {snapshot.Method}; availability: {snapshot.Availability}.");
    });

    Check("Windows RouteProvider egress and candidate enumeration", () =>
    {
        var routeProvider = new RouteProvider();
        var candidates = routeProvider.GetCandidates();
        Require(candidates.Count > 0, "No candidates returned by RouteProvider.");
        var defaultCount = candidates.Count(c => c.HasDefaultRoute);
        Console.WriteLine($"  RouteProvider found {candidates.Count} candidates ({defaultCount} with default route).");
        var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);
        Console.WriteLine($"  ScopePolicy AutoPrimary: {decision.Topology} - {decision.Summary} (Primary: {decision.PrimaryAdapterId})");
    });

    Check("Windows TaskbarCompanion query and placement behavior", () =>
    {
        var taskbar = TaskbarCompanion.QueryTaskbar();
        var workArea = new TaskbarRect(0, 0, 1920, 1040);
        var placement = TaskbarCompanion.CalculatePlacement(taskbar, 200, 40, workArea);
        if (taskbar.IsValid)
        {
            Console.WriteLine($"  Taskbar Edge: {taskbar.Edge}, AutoHide: {taskbar.IsAutoHide}, Bounds: {taskbar.Bounds.Left},{taskbar.Bounds.Top} - {taskbar.Bounds.Right},{taskbar.Bounds.Bottom}");
            Console.WriteLine($"  Companion placement: X={placement.X}, Y={placement.Y}, FloatingFallback={placement.UseFloatingFallback} ({placement.Reason})");
            Require(placement.X >= 0 && placement.Y >= 0, "Invalid coordinates.");
        }
        else
        {
            Console.WriteLine($"  Taskbar query unavailable in current session. Verified safe floating fallback: X={placement.X}, Y={placement.Y} ({placement.Reason})");
            Require(placement.UseFloatingFallback, "Must fall back to floating when taskbar is unavailable.");
        }
    });

    SpeedMeter.Checks.PerfFixtures.Run(Check);
}
Console.WriteLine($"Checks summary: {passed} passed, {skipped} skipped, {failed} failed. Total evaluated: {passed + skipped + failed}. Exit code: {Environment.ExitCode}.");
return Environment.ExitCode;
