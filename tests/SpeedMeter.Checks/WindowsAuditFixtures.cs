using SpeedMeter.Core;
using SpeedMeter.Windows;

namespace SpeedMeter.Checks;

/// <summary>Regressions for WS-02/03/07/10; these do not claim live route or ETW validation.</summary>
public static class WindowsAuditFixtures
{
    private static AdapterScopeCandidate Physical(string id, uint metric = 10, bool defaultRoute = true)
        => new(id, id, "Unverified inventory description", true, true, 1_000_000_000,
            InterfaceClassification.PhysicalEthernet, defaultRoute, metric);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireNoBoundary(CanonicalScopeDecision decision)
        => Require(decision.IsAmbiguous && decision.ActiveAdapterIds.Count == 0 && decision.PrimaryAdapterId is null,
            "An unresolved topology supplied a canonical accounting boundary.");

    public static void Run(Action<string, Action> check)
    {
        check("AUDIT WS-02: All aggregate hints fail closed without independence evidence", () =>
        {
            AdapterScopeCandidate[][] cases =
            [
                [],
                [Physical("a")],
                [Physical("a"), Physical("b")],
                [Physical("a"), Physical("a")],
                [Physical("a"), Physical("unknown-virtual") with { IsHardware = false }],
                [Physical("a"), Physical("bridge") with { Classification = InterfaceClassification.VirtualBridge }]
            ];
            foreach (var candidates in cases)
                RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.ValidatedAggregate, null, candidates));
        });

        check("AUDIT WS-03: Heuristic tunnel hints cannot override physical routes at any rank", () =>
        {
            foreach (var rank in new[] { 0u, 10u, uint.MaxValue })
            foreach (var hasDefault in new[] { false, true })
            {
                var vpn = Physical("vpn-hint", rank, hasDefault) with
                { Classification = InterfaceClassification.VpnTunnel, IsHardware = false };
                RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Physical("physical"), vpn]));
                RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [vpn, Physical("physical")]));
            }
        });

        check("AUDIT WS-03: Multiple tunnels and unknown virtual hints remain unresolved", () =>
        {
            var tunnel = Physical("vpn") with { Classification = InterfaceClassification.VpnTunnel };
            RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [tunnel, tunnel with { Id = "vpn2" }]));
            foreach (var hint in new[] { InterfaceClassification.VirtualBridge, InterfaceClassification.Other })
                RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null,
                    [Physical("a"), Physical("unknown") with { Classification = hint }]));
        });

        check("AUDIT WS-01/03: Multiple default candidates do not become complete from preference ranks", () =>
        {
            foreach (var rank in new[] { 0u, 10u, uint.MaxValue })
            {
                var decision = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Physical("a"), Physical("b", rank)]);
                RequireNoBoundary(decision);
                Require(decision.Topology == TopologyState.AmbiguousMultipleDefaults, "Multiple defaults not disclosed.");
            }
        });

        check("AUDIT WS-03: No route metadata never falls back to the fastest interface", () =>
        {
            RequireNoBoundary(ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null,
                [Physical("fast", defaultRoute: false), Physical("slow", defaultRoute: false) with { LinkSpeed = 1 }]));
        });

        check("AUDIT WS-02: Explicit selected tunnel remains scoped while duplicate identity fails closed", () =>
        {
            var vpn = Physical("vpn") with { Classification = InterfaceClassification.VpnTunnel, IsHardware = false };
            var selected = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "vpn", [Physical("physical"), vpn]);
            Require(!selected.IsAmbiguous && selected.ActiveAdapterIds.SequenceEqual(new[] { "vpn" }), "Explicit selected scope was replaced.");
            var duplicate = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "vpn", [vpn, vpn with { Name = "Conflicting identity" }]);
            Require(duplicate.IsAmbiguous && duplicate.ActiveAdapterIds.Count == 0 && duplicate.PrimaryAdapterId == "vpn",
                "Ambiguous selected identity was counted or silently replaced.");
        });

        check("AUDIT WS-10: Decision constructor snapshots membership and rejects exposed-list mutation", () =>
        {
            var callerMembers = new List<string> { "b", "a", "a" };
            var decision = new CanonicalScopeDecision(Guid.NewGuid(), 1, ScopeMode.RawDiagnostic,
                TopologyState.UnverifiedTopology, callerMembers, null, "Raw", "Unverified", true, DateTimeOffset.UtcNow);
            callerMembers.Clear();
            Require(decision.ActiveAdapterIds.SequenceEqual(new[] { "a", "b" }), "Caller mutation affected published membership.");
            var exposed = (IList<string>)decision.ActiveAdapterIds;
            var rejected = false;
            try { exposed[0] = "corrupted"; }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected && decision.ActiveAdapterIds[0] == "a", "Published membership remained mutable.");
        });

        check("AUDIT WS-10: Reordered diagnostic membership preserves revision; actual membership changes increment", () =>
        {
            var candidates = new[] { Physical("b"), Physical("a") };
            var first = ScopePolicy.Evaluate(ScopeMode.RawDiagnostic, null, candidates);
            candidates[0] = Physical("replacement");
            Require(first.ActiveAdapterIds.SequenceEqual(new[] { "a", "b" }), "Candidate array mutation changed prior decision.");
            var reordered = ScopePolicy.Evaluate(ScopeMode.RawDiagnostic, null, [Physical("a"), Physical("b")], first);
            Require(reordered.Revision == first.Revision && reordered.ScopeId == first.ScopeId, "Order-only change altered boundary.");
            var removed = ScopePolicy.Evaluate(ScopeMode.RawDiagnostic, null, [Physical("a")], reordered);
            Require(removed.Revision == reordered.Revision + 1, "Membership removal failed to advance revision.");
        });

        check("AUDIT WS-10: Selected missing/cleared/recovered membership has stable gap revisions", () =>
        {
            var active = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "a", [Physical("a")]);
            var missing = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "a", [], active);
            var stillMissing = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "a", [], missing);
            var clear = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, null, [], stillMissing);
            var stillClear = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, null, [], clear);
            var restored = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "a", [Physical("a")], stillClear);
            Require(missing.Revision == active.Revision + 1 && stillMissing.Revision == missing.Revision, "Missing state lacks a stable boundary.");
            Require(clear.Revision == missing.Revision + 1 && stillClear.Revision == clear.Revision, "Cleared selection lacks a stable boundary.");
            Require(restored.Revision == clear.Revision + 1, "Restored membership not versioned.");
        });

        check("AUDIT WS-10: Stable unavailable evaluations do not invent repeated revisions", () =>
        {
            foreach (var mode in new[] { ScopeMode.AutoPrimary, ScopeMode.ValidatedAggregate, ScopeMode.RawDiagnostic })
            {
                var first = ScopePolicy.Evaluate(mode, null, []);
                var second = ScopePolicy.Evaluate(mode, null, [], first);
                Require(second.Revision == first.Revision, "Unchanged unavailable topology advanced revision.");
            }
            var auto = ScopePolicy.Evaluate(ScopeMode.AutoPrimary, null, [Physical("a")]);
            var selected = ScopePolicy.Evaluate(ScopeMode.SelectedAdapter, "a", [Physical("a")], auto);
            Require(selected.Revision == auto.Revision + 1, "Mode change did not create a boundary.");
        });

        check("AUDIT WS-07: Process metadata and timestamp timezone do not establish live provider coverage", () =>
        {
            var created = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
            var process = new ProcessIdentity(42, created, "old-name", "old-path");
            Require(!AppAttributionSpike.IsPidRecycled(process, 42, created.ToOffset(TimeSpan.FromHours(6))),
                "The same instant in another timezone was treated as PID reuse.");
            Require(AppAttributionSpike.IsPidRecycled(process, 42, created.AddTicks(1)), "Distinct supplied creation epoch was ignored.");
            _ = AppAttributionSpike.IsElevated();
            Require(AppAttributionSpike.DetermineProviderCapability() == AttributionCompleteness.NotMeasured,
                "A token-role observation promoted an unimplemented provider to measured capability.");
        });
    }
}
