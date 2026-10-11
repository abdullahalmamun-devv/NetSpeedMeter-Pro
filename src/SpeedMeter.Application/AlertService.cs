using System;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

/// <summary>
/// Implement CAS and event append in one durable transaction. A false return means
/// another caller won the version/event identity race; it must not append an event.
/// </summary>
public interface IAlertStateStore
{
    Task<AlertIncidentState?> GetStateAsync(string key, CancellationToken ct = default);

    Task<bool> TryCommitAsync(
        string key,
        long expectedVersion,
        AlertIncidentState nextState,
        AlertEvent alertEvent,
        CancellationToken ct = default);

    /// <summary>Insert a history event once by exact EventId; quota source has already been recorded.</summary>
    Task<bool> TryRecordQuotaEventAsync(AlertEvent alertEvent, CancellationToken ct = default);
}

public sealed class StorageAlertStateStore : IAlertStateStore
{
    private readonly SpeedMeter.Storage.SqliteStorageEngine engine;

    public StorageAlertStateStore(SpeedMeter.Storage.SqliteStorageEngine engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Task<AlertIncidentState?> GetStateAsync(string key, CancellationToken ct = default) =>
        engine.GetAlertStateAsync(key, ct);

    public Task<bool> TryCommitAsync(
        string key,
        long expectedVersion,
        AlertIncidentState nextState,
        AlertEvent alertEvent,
        CancellationToken ct = default) =>
        engine.TryCommitAlertAsync(key, expectedVersion, nextState, alertEvent, ct);

    public Task<bool> TryRecordQuotaEventAsync(AlertEvent alertEvent, CancellationToken ct = default) =>
        engine.TryRecordQuotaAlertAsync(alertEvent, ct);
}

public sealed class InMemoryAlertStateStore : IAlertStateStore
{
    private readonly Dictionary<string, AlertIncidentState> states = new();
    private readonly HashSet<string> eventIds = new();
    public List<AlertEvent> Events { get; } = [];

    public Task<AlertIncidentState?> GetStateAsync(string key, CancellationToken ct = default)
    {
        lock (states) return Task.FromResult(states.GetValueOrDefault(key));
    }

    public Task<bool> TryCommitAsync(
        string key,
        long expectedVersion,
        AlertIncidentState nextState,
        AlertEvent alertEvent,
        CancellationToken ct = default)
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

public sealed class AlertService
{
    private readonly IAlertStateStore store;

    public AlertService(IAlertStateStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <returns>The committed event, or null when no new transition was committed.</returns>
    public async Task<AlertEvent?> EvaluateAsync(AlertRule rule, AlertObservation observation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(observation);
        rule.Validate();
        if (rule.Kind == AlertKind.QuotaThreshold) return null;
        if (!rule.Enabled || !observation.IsValid ||
            (rule.Kind != AlertKind.LocalDisconnect && !observation.ProbeEnabled)) return null;
        if (string.IsNullOrWhiteSpace(observation.EvidenceDescription)) return null;

        var condition = Classify(rule, observation);
        if (condition == Condition.Unknown) return null;

        var key = AlertIdentity.StateKey(rule);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var prior = await store.GetStateAsync(key, ct).ConfigureAwait(false) ??
                new AlertIncidentState(key, 0, null, 0, long.MinValue, 1);
            AlertIncidentState next;
            AlertEvent evt;
            if (prior.ActiveIncidentId is not null)
            {
                if (condition != Condition.Recovered) return null;
                evt = NewEvent(rule, observation, prior.ActiveIncidentId, prior.ActiveRuleRevision,
                    AlertEventPhase.Recovery, prior.ActiveIncidentId + ":recovery");
                next = prior with { Version = checked(prior.Version + 1), ActiveIncidentId = null, ActiveRuleRevision = 0 };
            }
            else
            {
                if (condition != Condition.Failing) return null;
                if (prior.LastTriggerUtcMs != long.MinValue &&
                    (observation.ObservedUtcMs < prior.LastTriggerUtcMs ||
                     observation.ObservedUtcMs - prior.LastTriggerUtcMs < rule.CooldownMs)) return null;
                var incidentId = key + "#" + prior.NextIncidentNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                evt = NewEvent(rule, observation, incidentId, rule.Revision,
                    AlertEventPhase.Trigger, incidentId + ":trigger");
                next = prior with
                {
                    Version = checked(prior.Version + 1),
                    ActiveIncidentId = incidentId,
                    ActiveRuleRevision = rule.Revision,
                    LastTriggerUtcMs = observation.ObservedUtcMs,
                    NextIncidentNumber = checked(prior.NextIncidentNumber + 1)
                };
            }

            if (await store.TryCommitAsync(key, prior.Version, next, evt, ct).ConfigureAwait(false)) return evt;
        }
        throw new InvalidOperationException("Alert state contention did not settle after eight attempts.");
    }

    /// <summary>
    /// Consumes an already persisted quota threshold. This does not evaluate usage,
    /// create a quota threshold, or replace the quota ledger's deduplication authority.
    /// </summary>
    public async Task<AlertEvent?> ConsumeQuotaThresholdAsync(
        AlertRule rule, QuotaThresholdEvent source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(source);
        rule.Validate();
        if (rule.Kind != AlertKind.QuotaThreshold || !rule.Enabled ||
            rule.RuleId != source.RuleId || rule.Revision != source.RuleRevision ||
            source.ThresholdBps <= 0 || source.CycleIdentifier.Length == 0) return null;
        var id = AlertIdentity.QuotaEventId(source);
        var evt = new AlertEvent(id, id, rule.RuleId, rule.Revision, rule.Kind,
            AlertEventPhase.Trigger, rule.ScopeId, rule.NetworkId, source.CrossedAtUtcMs,
            source.ThresholdBps / 100.0,
            $"Recorded usage crossed {source.ThresholdBps / 100.0:0.##}% of this quota in cycle {source.CycleIdentifier}; effective usage {source.EffectiveUsageBytes} bytes.",
            SuppressPopup(rule, source.CrossedAtUtcMs));
        return await store.TryRecordQuotaEventAsync(evt, ct).ConfigureAwait(false) ? evt : null;
    }

    private static AlertEvent NewEvent(AlertRule rule, AlertObservation observation, string incidentId,
        int revision, AlertEventPhase phase, string eventId) =>
        new(eventId, incidentId, rule.RuleId, revision, rule.Kind, phase,
            rule.ScopeId, rule.NetworkId, observation.ObservedUtcMs,
            rule.Kind == AlertKind.HighPingMean ? observation.SuccessfulMeanRttMs : null,
            observation.EvidenceDescription,
            SuppressPopup(rule, observation.ObservedUtcMs));

    private static bool SuppressPopup(AlertRule rule, long utcMs) =>
        (rule.Channels & AlertChannels.Popup) == 0 || rule.QuietHours?.Contains(utcMs) == true;

    private enum Condition { Unknown, Failing, Recovered }

    private static Condition Classify(AlertRule rule, AlertObservation o)
    {
        if (o.ValidEvidenceDurationMs < rule.PersistenceMs) return Condition.Unknown;
        return rule.Kind switch
        {
            AlertKind.LocalDisconnect => o.LocalConnected switch
            {
                false => Condition.Failing,
                true => Condition.Recovered,
                _ => Condition.Unknown
            },
            AlertKind.ReachabilityFailure when o.ConfiguredChecks >= rule.MinimumSamples =>
                o.FailedChecks == o.ConfiguredChecks ? Condition.Failing :
                o.FailedChecks == 0 ? Condition.Recovered : Condition.Unknown,
            AlertKind.HighPingMean when o.WindowDurationMs >= rule.ObservationWindowMs &&
                o.SuccessfulReplies >= rule.MinimumSamples &&
                o.SuccessfulMeanRttMs is double mean && double.IsFinite(mean) && mean >= 0 =>
                mean > rule.TriggerThreshold ? Condition.Failing :
                mean < rule.RecoveryThreshold ? Condition.Recovered : Condition.Unknown,
            _ => Condition.Unknown
        };
    }
}
