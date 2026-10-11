using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

/// <summary>Deterministic AC-C01–C07 checks; every probe is injected and no public request is sent.</summary>
public static class ConnectivityFixtures
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public static void Run(Action<string, Action> check)
    {
        check("W4-CONN: endpoint registry requires approved HTTPS usage and secure target", EndpointRegistry);
        check("W4-CONN AC-C01: blocked ICMP with reachable HTTPS never claims outage", BlockedIcmp);
        check("W4-CONN AC-C02: three all-fail rounds confirm; two successes recover", FailureAndRecovery);
        check("W4-CONN AC-C03: portal redirect is sign-in suspected, not root-cause certainty", CaptivePortal);
        check("W4-CONN AC-C04: sleep closes observed incident at last check with uncertain end", SleepClosesAtLastObservation);
        check("W4-CONN AC-C05: default-off and metered suppression make no requests", ConsentAndMetering);
        check("W4-CONN AC-C06: proxy authentication is incomplete, not global outage", ProxyIncomplete);
        check("W4-CONN AC-C07: missing duration is excluded and coverage shown", AvailabilityCoverage);
        check("W4-CONN: stale success expires rather than staying reachable", StaleSuccess);
        check("W4-CONN: scope change cancels and rejects old round", ScopeGeneration);
        check("W4-CONN: ignoring cancellation cannot start concurrent endpoint request", StuckProvider);
    }

    private static ConnectivityEndpoint Https(string id = "a") => new(id, id, ProbeProtocol.Https,
        $"https://{id}.example.test/reachability", 204, TimeSpan.FromMilliseconds(80),
        TimeSpan.FromSeconds(30), "Fixture-owned endpoint; test provider only");

    private static ConnectivityEndpoint Icmp() => new("icmp", "ICMP fixture", ProbeProtocol.Icmp,
        "icmp.example.test", null, TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(30),
        "Fixture-owned host; test provider only");

    private static ConnectivityEndpointRegistry Registry(params ConnectivityEndpoint[] endpoints) => new(endpoints);

    private static ClockReading Clock(double mono) => new(mono, Start.AddSeconds(mono));

    private static ProbeObservation Observation(string id, ProbeOutcome outcome, double mono) =>
        new(id, outcome, Start.AddSeconds(mono), mono);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class FakeProvider(Func<ConnectivityEndpoint, CancellationToken, Task<ProbeResult>> probe)
        : IConnectivityProbeProvider
    {
        public int Calls { get; private set; }
        public Task<ProbeResult> ProbeAsync(ConnectivityEndpoint endpoint, CancellationToken cancellationToken)
        {
            Calls++;
            return probe(endpoint, cancellationToken);
        }
    }

    private static void EndpointRegistry()
    {
        Require(Registry().Endpoints.Count == 0, "Empty endpoint registry must be legal and issue no requests.");
        try { _ = new ConnectivityEndpoint("bad", "Bad", ProbeProtocol.Https,
            "http://example.test/", 204, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), "approved");
            throw new Exception("Cleartext endpoint accepted."); }
        catch (ArgumentException ex) when (ex.Message.Contains("HTTPS", StringComparison.Ordinal)) { }
        try { _ = new ConnectivityEndpoint("bad", "Bad", ProbeProtocol.Https,
            "https://example.test/", 204, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "approved");
            throw new Exception("Too-frequent endpoint accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { _ = Registry(Icmp()); throw new Exception("ICMP-only registry accepted."); }
        catch (ArgumentException ex) when (ex.Message.Contains("HTTPS", StringComparison.Ordinal)) { }
    }

    private static void BlockedIcmp()
    {
        var tracker = new ConnectivityTracker(Registry(Https(), Icmp()));
        tracker.SetContext("nic-a", LocalLinkState.Connected, true, false, Start, 0);
        tracker.RecordRound([Observation("a", ProbeOutcome.Success, 0),
            Observation("icmp", ProbeOutcome.IcmpBlocked, 0)], Start, 0, TimeSpan.FromSeconds(30));
        // ICMP refusal is an incomplete path, but HTTPS evidence prevents a global outage.
        Require(tracker.Snapshot.ActiveIncident is null && tracker.Snapshot.State != ConnectivityState.NoLocalConnection,
            "ICMP refusal became a global outage.");
    }

    private static void FailureAndRecovery()
    {
        var tracker = new ConnectivityTracker(Registry(Https("a"), Https("b")));
        tracker.SetContext("nic-a", LocalLinkState.Connected, true, false, Start, 0);
        for (var i = 0; i < 3; i++)
        {
            var mono = i * 30d;
            tracker.RecordRound([Observation("a", ProbeOutcome.Timeout, mono),
                Observation("b", ProbeOutcome.DnsFailure, mono)], Start.AddSeconds(mono), mono,
                TimeSpan.FromSeconds(30));
            if (i < 2) Require(tracker.Snapshot.ActiveIncident is null, "Outage confirmed too early.");
        }
        var active = tracker.Snapshot.ActiveIncident;
        Require(active is not null && active.FirstFailedObservationUtc == Start &&
            active.ConfirmationUtc == Start.AddSeconds(60), "First failure and confirmation were conflated.");
        tracker.RecordRound([Observation("a", ProbeOutcome.Success, 90),
            Observation("b", ProbeOutcome.Timeout, 90)], Start.AddSeconds(90), 90,
            TimeSpan.FromSeconds(30));
        Require(tracker.Snapshot.ActiveIncident is not null, "Recovered after only one success.");
        tracker.RecordRound([Observation("a", ProbeOutcome.Success, 120),
            Observation("b", ProbeOutcome.Success, 120)], Start.AddSeconds(120), 120,
            TimeSpan.FromSeconds(30));
        var ended = tracker.Snapshot.CompletedIncidents.Single();
        Require(ended.FirstSuccessfulObservationUtc == Start.AddSeconds(90) &&
            ended.FinalizationUtc == Start.AddSeconds(120) && !ended.EndUncertain &&
            tracker.Snapshot.State == ConnectivityState.Reachable, "Recovery timing or state is wrong.");
        Require(Math.Abs(tracker.Snapshot.UnavailableObservedSeconds - 90) < 0.0001,
            "Unavailable duration should end at first successful observation.");
    }

    private static void CaptivePortal()
    {
        var tracker = new ConnectivityTracker(Registry(Https()));
        tracker.SetContext("wifi", LocalLinkState.Connected, true, false, Start, 0);
        tracker.RecordRound([Observation("a", ProbeOutcome.PortalRedirect, 0)], Start, 0,
            TimeSpan.FromSeconds(30));
        Require(tracker.Snapshot.State == ConnectivityState.SignInSuspected &&
            tracker.Snapshot.ActiveIncident is null, "Portal evidence must remain a suspicion.");
    }

    private static void SleepClosesAtLastObservation()
    {
        var tracker = new ConnectivityTracker(Registry(Https()));
        tracker.SetContext("nic-a", LocalLinkState.Connected, true, false, Start, 0);
        foreach (var mono in new[] { 0d, 30d, 60d, 90d })
            tracker.RecordRound([Observation("a", ProbeOutcome.Timeout, mono)], Start.AddSeconds(mono),
                mono, TimeSpan.FromSeconds(30));
        tracker.MarkObservationGap(Start.AddHours(1), 3600);
        var ended = tracker.Snapshot.CompletedIncidents.Single();
        Require(ended.EndUncertain && ended.FinalizationUtc == Start.AddSeconds(90) &&
            Math.Abs(tracker.Snapshot.UnavailableObservedSeconds - 90) < 0.0001,
            "Sleep duration was incorrectly counted as observed outage.");
    }

    private static void ConsentAndMetering()
    {
        var fake = new FakeProvider((_, _) => Task.FromResult(new ProbeResult(ProbeOutcome.Success)));
        using var service = new ConnectivityService(Registry(Https()), fake);
        service.TickAsync(Clock(0), "nic-a", LocalLinkState.Connected, false).GetAwaiter().GetResult();
        Require(fake.Calls == 0 && service.Snapshot.AvailabilityLabel == "Local link only",
            "Default off sent a request or claimed remote uptime.");
        service.SetConfiguration(true);
        service.TickAsync(Clock(1), "nic-a", LocalLinkState.Connected, true).GetAwaiter().GetResult();
        Require(fake.Calls == 0 && service.Snapshot.RemoteChecksSuppressedByMetering,
            "Metered default sent a request.");
        service.SetConfiguration(true, true);
        service.TickAsync(Clock(2), "nic-a", LocalLinkState.Connected, true).GetAwaiter().GetResult();
        Require(fake.Calls == 1, "Explicit metered opt-in did not permit a check.");
    }

    private static void ProxyIncomplete()
    {
        var tracker = new ConnectivityTracker(Registry(Https()));
        tracker.SetContext("nic-a", LocalLinkState.Connected, true, false, Start, 0);
        foreach (var mono in new[] { 0d, 30d, 60d })
            tracker.RecordRound([Observation("a", ProbeOutcome.ProxyAuthenticationRequired, mono)],
                Start.AddSeconds(mono), mono, TimeSpan.FromSeconds(30));
        Require(tracker.Snapshot.ActiveIncident is null && tracker.Snapshot.State == ConnectivityState.Limited,
            "Proxy auth was treated as certain internet outage.");
    }

    private static void AvailabilityCoverage()
    {
        var result = ConnectivityAvailabilityCalculator.Calculate(90, 30, 300);
        Require(Math.Abs(result.ObservedAvailability!.Value - .75) < .00001 &&
            Math.Abs(result.MeasuredCoverage - .4) < .00001,
            "Unknown duration entered the availability denominator.");
        var none = ConnectivityAvailabilityCalculator.Calculate(0, 0, 300);
        Require(none.ObservedAvailability is null && none.MeasuredCoverage == 0,
            "No coverage must not appear as 0% internet availability.");
    }

    private static void StaleSuccess()
    {
        var tracker = new ConnectivityTracker(Registry(Https()), TimeSpan.FromSeconds(90));
        tracker.SetContext("nic-a", LocalLinkState.Connected, true, false, Start, 0);
        tracker.RecordRound([Observation("a", ProbeOutcome.Success, 0)], Start, 0,
            TimeSpan.FromSeconds(30));
        tracker.RecordRound([Observation("a", ProbeOutcome.Success, 30)], Start.AddSeconds(30), 30,
            TimeSpan.FromSeconds(30));
        Require(tracker.Snapshot.State == ConnectivityState.Reachable, "Two successes should reach reachable.");
        tracker.Expire(Start.AddSeconds(121), 121);
        Require(tracker.Snapshot.State == ConnectivityState.Unknown &&
            tracker.Snapshot.ReachableObservedSeconds <= 30.0001,
            "Stale green status or invented reachable duration.");
    }

    private static void ScopeGeneration()
    {
        var pending = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeProvider((_, _) => pending.Task);
        using var service = new ConnectivityService(Registry(Https()), fake);
        service.SetConfiguration(true);
        var old = service.TickAsync(Clock(0), "nic-a", LocalLinkState.Connected, false);
        service.TickAsync(Clock(1), "nic-b", LocalLinkState.Connected, false).GetAwaiter().GetResult();
        pending.SetResult(new ProbeResult(ProbeOutcome.Success));
        old.GetAwaiter().GetResult();
        Require(service.Snapshot.ScopeId == "nic-b" && service.Snapshot.LastSuccessfulCheckUtc is null,
            "Old scope result overwrote new context.");
    }

    private static void StuckProvider()
    {
        var pending = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeProvider((_, _) => pending.Task);
        using var service = new ConnectivityService(Registry(Https()), fake);
        service.SetConfiguration(true);
        var before = DateTime.UtcNow;
        service.TickAsync(Clock(0), "nic-a", LocalLinkState.Connected, false).GetAwaiter().GetResult();
        Require((DateTime.UtcNow - before) < TimeSpan.FromSeconds(2), "Ignoring provider cancellation blocked a round.");
        service.TickAsync(Clock(30), "nic-a", LocalLinkState.Connected, false).GetAwaiter().GetResult();
        Require(fake.Calls == 1, "A second request started while the first remained outstanding.");
        pending.SetResult(new ProbeResult(ProbeOutcome.Canceled));
    }
}
