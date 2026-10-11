using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.Checks;

public static class AlertFixtures
{
    private const long T = 1_800_000_000_000;

    public static void Run(Action<string, Action> check)
    {
        check("W4-ALERT local disconnect debounces and recovers once", DisconnectRecovery);
        check("W4-ALERT reachability requires configured multi-check evidence", ReachabilityEvidence);
        check("W4-ALERT high ping uses successful mean and ten replies for 30 seconds", HighPingEvidence);
        check("W4-ALERT stale and paused observations cannot trigger or recover", InvalidObservations);
        check("W4-ALERT incident survives restart without duplicate trigger", RestartDedup);
        check("W4-ALERT cooldown applies after recovery", Cooldown);
        check("W4-ALERT quiet hours retain history and suppress popup", QuietHours);
        check("W4-ALERT rule revision does not fork an active incident", RuleRevision);
        check("W4-ALERT quota consumes authoritative threshold event once", QuotaConsumption);
        check("W4-ALERT disabled probe cannot trigger", DisabledProbe);
    }

    private static AlertRule Rule(AlertKind kind) => kind switch
    {
        AlertKind.HighPingMean => new("ping", 1, kind, "scope-A", "network-A", true,
            150, 120, 30_000, 10, 30_000, 600_000),
        AlertKind.ReachabilityFailure => new("reach", 1, kind, "scope-A", "network-A", true,
            0, 0, 5_000, 2, 5_000, 600_000),
        AlertKind.QuotaThreshold => new("quota", 1, kind, "scope-A", "network-A", true,
            0, 0, 1, 0, 1, 600_000),
        _ => new("local", 1, kind, "scope-A", "network-A", true,
            0, 0, 1_000, 0, 3_000, 600_000)
    };

    private static AlertObservation Observation(long time = T, long duration = 30_000,
        int replies = 10, double? mean = 160, bool? connected = false,
        int checks = 2, int failed = 2, bool fresh = true, bool active = true,
        bool enabled = true, long window = 30_000) =>
        new(time, fresh, true, active, enabled, duration, window, replies, mean,
            connected, checks, failed, "Observed selected scope and endpoint over valid elapsed window");

    private static AlertEvent? Evaluate(AlertService service, AlertRule rule, AlertObservation observation) =>
        service.EvaluateAsync(rule, observation).GetAwaiter().GetResult();

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void DisconnectRecovery()
    {
        var store = new MemoryStore(); var service = new AlertService(store); var rule = Rule(AlertKind.LocalDisconnect);
        Require(Evaluate(service, rule, Observation(duration: 2_999)) is null, "Debounce fired early.");
        var trigger = Evaluate(service, rule, Observation(duration: 3_000));
        Require(trigger?.Phase == AlertEventPhase.Trigger, "No disconnect event.");
        Require(Evaluate(service, rule, Observation(time: T + 1_000)) is null, "Repeated disconnect triggered again.");
        var recovery = Evaluate(service, rule, Observation(time: T + 4_000, connected: true));
        Require(recovery?.Phase == AlertEventPhase.Recovery && recovery.IncidentId == trigger!.IncidentId,
            "Recovery did not close the active incident.");
        Require(store.Events.Count == 2, "Expected one trigger and one recovery in history.");
    }

    private static void ReachabilityEvidence()
    {
        var service = new AlertService(new MemoryStore()); var rule = Rule(AlertKind.ReachabilityFailure);
        Require(Evaluate(service, rule, Observation(checks: 1, failed: 1)) is null, "One endpoint was treated as an outage.");
        Require(Evaluate(service, rule, Observation(checks: 2, failed: 1)) is null, "Mixed checks triggered an outage.");
        var trigger = Evaluate(service, rule, Observation());
        Require(trigger?.Kind == AlertKind.ReachabilityFailure, "Two failed checks did not trigger.");
        Require(Evaluate(service, rule, Observation(time: T + 10_000, failed: 1)) is null,
            "Partial recovery closed incident.");
        Require(Evaluate(service, rule, Observation(time: T + 20_000, failed: 0))?.Phase == AlertEventPhase.Recovery,
            "All-success evidence did not recover.");
    }

    private static void HighPingEvidence()
    {
        var service = new AlertService(new MemoryStore()); var rule = Rule(AlertKind.HighPingMean);
        Require(Evaluate(service, rule, Observation(replies: 9)) is null, "Nine replies triggered.");
        Require(Evaluate(service, rule, Observation(window: 29_999)) is null, "Short window triggered.");
        Require(Evaluate(service, rule, Observation(duration: 29_999)) is null, "Short persistence triggered.");
        Require(Evaluate(service, rule, Observation(mean: 150)) is null, "At-threshold mean triggered.");
        Require(Evaluate(service, rule, Observation(mean: double.NaN)) is null, "Invalid mean triggered.");
        Require(Evaluate(service, rule, Observation())?.Phase == AlertEventPhase.Trigger, "Sustained mean did not trigger.");
        Require(Evaluate(service, rule, Observation(time: T + 1_000, mean: 120)) is null, "At-recovery mean closed.");
        Require(Evaluate(service, rule, Observation(time: T + 2_000, mean: 119))?.Phase == AlertEventPhase.Recovery,
            "Recovery mean did not close.");
    }

    private static void InvalidObservations()
    {
        var service = new AlertService(new MemoryStore()); var rule = Rule(AlertKind.HighPingMean);
        Require(Evaluate(service, rule, Observation(fresh: false)) is null, "Stale trigger.");
        Require(Evaluate(service, rule, Observation(active: false)) is null, "Paused trigger.");
        Require(Evaluate(service, rule, Observation()) is not null, "Missing valid trigger.");
        Require(Evaluate(service, rule, Observation(time: T + 10_000, mean: 100, fresh: false)) is null,
            "Stale recovery.");
        Require(Evaluate(service, rule, Observation(time: T + 20_000, mean: 100, active: false)) is null,
            "Paused recovery.");
        Require(Evaluate(service, rule, Observation(time: T + 30_000, mean: 100))?.Phase == AlertEventPhase.Recovery,
            "Valid recovery missing.");
    }

    private static void RestartDedup()
    {
        var store = new MemoryStore(); var rule = Rule(AlertKind.LocalDisconnect);
        Require(Evaluate(new AlertService(store), rule, Observation()) is not null, "Trigger missing.");
        Require(Evaluate(new AlertService(store), rule, Observation(time: T + 20_000)) is null,
            "Restart replayed active incident.");
        Require(store.Events.Count == 1, "Restart added duplicate history.");
    }

    private static void Cooldown()
    {
        var store = new MemoryStore(); var service = new AlertService(store); var rule = Rule(AlertKind.LocalDisconnect);
        _ = Evaluate(service, rule, Observation());
        _ = Evaluate(service, rule, Observation(time: T + 10_000, connected: true));
        Require(Evaluate(new AlertService(store), rule, Observation(time: T + 599_999)) is null,
            "Cooldown lost across restart.");
        Require(Evaluate(new AlertService(store), rule, Observation(time: T + 600_000))?.Phase == AlertEventPhase.Trigger,
            "Cooldown blocked at expiry.");
    }

    private static void QuietHours()
    {
        var time = new DateTimeOffset(2026, 10, 11, 23, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var rule = Rule(AlertKind.LocalDisconnect) with
        {
            QuietHours = new AlertQuietHours(new TimeOnly(22, 0), new TimeOnly(6, 0), "UTC")
        };
        var store = new MemoryStore();
        var evt = Evaluate(new AlertService(store), rule, Observation(time: time));
        Require(evt?.SuppressPopup == true && store.Events.Count == 1, "Quiet hours erased history or showed popup.");
    }

    private static void RuleRevision()
    {
        var store = new MemoryStore(); var service = new AlertService(store); var old = Rule(AlertKind.LocalDisconnect);
        var trigger = Evaluate(service, old, Observation());
        var revised = old with { Revision = 2 };
        Require(Evaluate(service, revised, Observation(time: T + 20_000)) is null,
            "Revision forked an active incident.");
        var recovery = Evaluate(service, revised, Observation(time: T + 30_000, connected: true));
        Require(recovery?.IncidentId == trigger?.IncidentId && recovery?.RuleRevision == 1,
            "Revision failed to recover original incident identity.");
        Require(Evaluate(service, revised, Observation(time: T + 600_000))?.RuleRevision == 2,
            "Revised rule did not arm after recovery and cooldown.");
    }

    private static void QuotaConsumption()
    {
        var store = new MemoryStore(); var service = new AlertService(store); var rule = Rule(AlertKind.QuotaThreshold);
        var source = new QuotaThresholdEvent("random-event-id", rule.RuleId, rule.Revision,
            "cycle-1", 8000, T, 8_000);
        var first = service.ConsumeQuotaThresholdAsync(rule, source).GetAwaiter().GetResult();
        var replay = new AlertService(store).ConsumeQuotaThresholdAsync(rule,
            source with { EventId = "different-random-id" }).GetAwaiter().GetResult();
        Require(first is not null && replay is null && store.Events.Count == 1,
            "Quota event dedup used random source event ID instead of threshold identity.");
        Require(Evaluate(service, rule, Observation()) is null, "Quota was evaluated as a second threshold system.");
    }

    private static void DisabledProbe()
    {
        foreach (var kind in new[] { AlertKind.ReachabilityFailure, AlertKind.HighPingMean })
            Require(Evaluate(new AlertService(new MemoryStore()), Rule(kind), Observation(enabled: false)) is null,
                "Disabled probe triggered an incident.");
    }

    private sealed class MemoryStore : IAlertStateStore
    {
        private readonly Dictionary<string, AlertIncidentState> states = new();
        private readonly HashSet<string> eventIds = new();
        public List<AlertEvent> Events { get; } = [];

        public Task<AlertIncidentState?> GetStateAsync(string key, CancellationToken ct = default)
        {
            lock (states) return Task.FromResult(states.GetValueOrDefault(key));
        }

        public Task<bool> TryCommitAsync(string key, long expectedVersion,
            AlertIncidentState nextState, AlertEvent alertEvent, CancellationToken ct = default)
        {
            lock (states)
            {
                var actualVersion = states.GetValueOrDefault(key)?.Version ?? 0;
                if (actualVersion != expectedVersion || !eventIds.Add(alertEvent.EventId)) return Task.FromResult(false);
                states[key] = nextState;
                Events.Add(alertEvent);
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryRecordQuotaEventAsync(AlertEvent alertEvent, CancellationToken ct = default)
        {
            lock (states)
            {
                if (!eventIds.Add(alertEvent.EventId)) return Task.FromResult(false);
                Events.Add(alertEvent);
                return Task.FromResult(true);
            }
        }
    }
}
