using System;
using System.Collections.Generic;
using System.Linq;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

public static class ScopeFixtures
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static AdapterScopeCandidate Eth(string id = "eth1", bool up = true, bool defRoute = true, uint metric = 10)
        => new(id, "Ethernet", "Intel Gigabit", up, true, 1_000_000_000, InterfaceClassification.PhysicalEthernet, defRoute, metric);

    private static AdapterScopeCandidate Wifi(string id = "wifi1", bool up = true, bool defRoute = true, uint metric = 20)
        => new(id, "Wi-Fi", "Intel Wireless AC", up, true, 866_000_000, InterfaceClassification.PhysicalWireless, defRoute, metric);

    private static AdapterScopeCandidate Vpn(string id = "vpn1", bool up = true, bool defRoute = true, uint metric = 5)
        => new(id, "WireGuard Tunnel", "WireGuard Virtual Adapter", up, false, 100_000_000, InterfaceClassification.VpnTunnel, defRoute, metric);

    private static AdapterScopeCandidate Virtual(string id = "veth1", bool up = true)
        => new(id, "vEthernet (Default Switch)", "Hyper-V Virtual Switch", up, false, 10_000_000_000, InterfaceClassification.VirtualBridge, false, uint.MaxValue);

    public static void Run(Action<string, Action> check)
    {
        check("SCOPE: Ethernet only selects Ethernet as SinglePrimary", () =>
        {
            var candidates = new[] { Eth() };
            var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);

            Require(decision.Topology == TopologyState.SinglePrimary, "Expected SinglePrimary");
            Require(decision.PrimaryAdapterId == "eth1", "Expected eth1 as primary");
            Require(!decision.IsAmbiguous, "Must not be ambiguous");
        });

        check("SCOPE: Wi-Fi only selects Wi-Fi as SinglePrimary", () =>
        {
            var candidates = new[] { Wifi() };
            var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);

            Require(decision.Topology == TopologyState.SinglePrimary, "Expected SinglePrimary");
            Require(decision.PrimaryAdapterId == "wifi1", "Expected wifi1 as primary");
            Require(!decision.IsAmbiguous, "Must not be ambiguous");
        });

        check("SCOPE: Ethernet to Wi-Fi transition increments scope revision", () =>
        {
            var d1 = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Eth(up: true), Wifi(up: false)]);
            Require(d1.PrimaryAdapterId == "eth1", "Expected eth1 initially");

            var d2 = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Eth(up: false), Wifi(up: true)], d1);
            Require(d2.PrimaryAdapterId == "wifi1", "Expected wifi1 after switch");
            Require(d2.Revision > d1.Revision, "Scope revision must increment on adapter transition to force rebaseline");
        });

        check("SCOPE: Additional default candidate requires explicit selection despite lower preference rank", () =>
        {
            var d1 = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Wifi(metric: 20)]);

            var d2 = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Eth(metric: 10), Wifi(metric: 20)], d1);
            // Audit WS-01/03: the old assertion incorrectly treated synthetic metric ranks as route proof.
            Require(d2.PrimaryAdapterId is null && d2.IsAmbiguous && d2.ActiveAdapterIds.Count == 0,
                "Multiple candidates must not silently select a canonical boundary.");
            Require(d2.Revision > d1.Revision, "Revision must increment when a boundary becomes unresolved");
        });

        check("SCOPE: Selected adapter missing never silently switches", () =>
        {
            var candidates = new[] { Eth("eth1", up: true), Wifi("wifi1", up: true) };
            var d1 = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "eth1", candidates);
            Require(d1.Topology == TopologyState.SinglePrimary && d1.PrimaryAdapterId == "eth1", "eth1 must be selected");

            var d2 = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "eth1", [Eth("eth1", up: false), Wifi("wifi1", up: true)], d1);
            Require(d2.Topology == TopologyState.SelectedAdapterMissing, "Must report SelectedAdapterMissing");
            Require(d2.PrimaryAdapterId == "eth1", "Must preserve selected adapter ID");
            Require(d2.ActiveAdapterIds.Count == 0, "Active adapters must be empty when selected is down");
        });

        check("SCOPE: Selected adapter returns resumes without silent switch", () =>
        {
            var d1 = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "eth1", [Eth("eth1", up: false)]);
            Require(d1.Topology == TopologyState.SelectedAdapterMissing, "Must be missing");

            var d2 = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "eth1", [Eth("eth1", up: true)], d1);
            Require(d2.Topology == TopologyState.SinglePrimary, "Must resume SinglePrimary");
            Require(d2.PrimaryAdapterId == "eth1", "Must be eth1");
        });

        check("SCOPE: Ambiguous equal-metric default routes flagged explicitly", () =>
        {
            var candidates = new[] { Eth(metric: 15), Wifi(metric: 15) };
            var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);

            Require(decision.Topology == TopologyState.AmbiguousMultipleDefaults, "Expected AmbiguousMultipleDefaults");
            Require(decision.IsAmbiguous, "IsAmbiguous must be true");
        });

        check("SCOPE: VPN hint requires explicit boundary selection without automatic tunnel priority", () =>
        {
            var candidates = new[] { Eth(metric: 25), Vpn(metric: 5) };
            var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, candidates);

            // Audit WS-03: one tunnel label is not evidence of full-tunnel coverage or canonical usage.
            Require(decision.Topology == TopologyState.UnverifiedTopology && decision.IsAmbiguous, "VPN topology must be unverified");
            Require(decision.PrimaryAdapterId is null && decision.ActiveAdapterIds.Count == 0, "Must not invent VPN priority or sum boundaries");
        });

        check("SCOPE: ValidatedAggregate rejects aggregation when VPN or virtual bridge is active", () =>
        {
            var candidates = new[] { Eth(), Wifi(), Virtual() };
            var decision = ScopePolicy.Evaluate(ScopeMode.ValidatedAggregate, null, candidates);

            Require(decision.ActiveAdapterIds.Count == 0, "Aggregation must be rejected when virtual bridge exists");
            Require(decision.IsAmbiguous, "Must be flagged ambiguous");
        });

        check("SCOPE: Hardware labels cannot establish independent aggregate boundaries", () =>
        {
            var candidates = new[] { Eth("eth1"), Eth("eth2") };
            var decision = ScopePolicy.Evaluate(ScopeMode.ValidatedAggregate, null, candidates);

            // Audit WS-02: the old fixture named these disjoint without supplying independence evidence.
            Require(decision.IsAmbiguous && decision.Topology == TopologyState.UnverifiedTopology, "Independence is unverified");
            Require(decision.ActiveAdapterIds.Count == 0, "Unverified hardware must not enter aggregate accounting");
        });

        check("SCOPE: Explicit adapter ID selection rebaselines MeasurementEngine (no revision integration)", () =>
        {
            var engine = new MeasurementEngine();
            engine.SelectScope("eth1");

            var utc = DateTimeOffset.UtcNow;
            engine.Sample(new AdapterCounter("eth1", "Eth", "Intel", true, 1000, 500, 1_000_000_000), 0, utc, 1);
            var live = engine.Sample(new AdapterCounter("eth1", "Eth", "Intel", true, 2000, 1000, 1_000_000_000), 1, utc.AddSeconds(1), 1);
            Require(live.IsValid && live.Download == 1000.0, "Live on eth1");

            // Scope transition to wifi1
            engine.SelectScope("wifi1");

            var firstWifi = engine.Sample(new AdapterCounter("wifi1", "Wifi", "Intel", true, 50_000_000, 25_000_000, 866_000_000), 2, utc.AddSeconds(2), 1);
            Require(firstWifi.State == ReadingState.Measuring, "First sample after scope switch must be Measuring");
            Require(firstWifi.Download is null, "No rate or spike allowed on new scope baseline!");
            Require(firstWifi.Received == 0, "Session total reset on explicit scope change");

            var secondWifi = engine.Sample(new AdapterCounter("wifi1", "Wifi", "Intel", true, 50_002_000, 25_001_000, 866_000_000), 3, utc.AddSeconds(3), 1);
            Require(secondWifi.IsValid && secondWifi.Download == 2000.0, "Rate must be computed only from new scope delta");
        });
    }
}
