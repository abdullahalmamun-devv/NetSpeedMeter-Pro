using System;

namespace SpeedMeter.Core;

public enum AlertKind
{
    QuotaThreshold,
    LocalDisconnect,
    ReachabilityFailure,
    HighPingMean
}

public enum AlertEventPhase { Trigger, Recovery }

[Flags]
public enum AlertChannels { History = 1, Popup = 2, Sound = 4 }

/// <summary>Local clock interval; an equal start and end disables quiet hours.</summary>
public sealed record AlertQuietHours(TimeOnly Start, TimeOnly End, string TimezoneId)
{
    public bool Contains(long utcMs)
    {
        if (Start == End) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimezoneId);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(utcMs), zone).TimeOfDay;
        var start = Start.ToTimeSpan();
        var end = End.ToTimeSpan();
        return start < end ? local >= start && local < end : local >= start || local < end;
    }
}

/// <summary>
/// A V1 alert rule. Scope and network identities are canonical opaque identifiers,
/// never display names. Duration fields are elapsed evidence supplied by the provider.
/// </summary>
public sealed record AlertRule(
    string RuleId,
    int Revision,
    AlertKind Kind,
    string ScopeId,
    string NetworkId,
    bool Enabled,
    double TriggerThreshold,
    double RecoveryThreshold,
    long ObservationWindowMs,
    int MinimumSamples,
    long PersistenceMs,
    long CooldownMs = 600_000,
    AlertQuietHours? QuietHours = null,
    AlertChannels Channels = AlertChannels.History | AlertChannels.Popup)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(NetworkId);
        if (Revision <= 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        if (ObservationWindowMs <= 0 || PersistenceMs <= 0 || CooldownMs < 0 || MinimumSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(ObservationWindowMs));
        if (!double.IsFinite(TriggerThreshold) || !double.IsFinite(RecoveryThreshold))
            throw new ArgumentOutOfRangeException(nameof(TriggerThreshold));
        if ((Channels & AlertChannels.History) == 0 || (Channels & ~(AlertChannels.History | AlertChannels.Popup | AlertChannels.Sound)) != 0)
            throw new ArgumentOutOfRangeException(nameof(Channels));
        if (Kind == AlertKind.HighPingMean &&
            (ObservationWindowMs < 30_000 || PersistenceMs < 30_000 || MinimumSamples < 10 ||
             TriggerThreshold <= RecoveryThreshold || RecoveryThreshold < 0))
            throw new ArgumentException("V1 high ping requires a 30-second successful-mean window, 10 replies, and recovery hysteresis.");
        if (Kind == AlertKind.ReachabilityFailure && MinimumSamples < 2)
            throw new ArgumentException("Reachability failure requires at least two configured checks.");
        if (QuietHours is not null) _ = TimeZoneInfo.FindSystemTimeZoneById(QuietHours.TimezoneId);
    }
}

/// <summary>
/// A fully formed provider observation. ValidEvidenceDurationMs is continuous elapsed
/// duration of the indicated condition; stale or interrupted runs must reset it.
/// No field here is inferred from wall-clock timestamp subtraction.
/// </summary>
public sealed record AlertObservation(
    long ObservedUtcMs,
    bool Fresh,
    bool Available,
    bool MonitoringActive,
    bool ProbeEnabled,
    long ValidEvidenceDurationMs,
    long WindowDurationMs,
    int SuccessfulReplies,
    double? SuccessfulMeanRttMs,
    bool? LocalConnected,
    int ConfiguredChecks,
    int FailedChecks,
    string EvidenceDescription)
{
    public bool IsValid => Fresh && Available && MonitoringActive && ValidEvidenceDurationMs >= 0 &&
                           WindowDurationMs >= 0 && SuccessfulReplies >= 0 &&
                           ConfiguredChecks >= 0 && FailedChecks >= 0 && FailedChecks <= ConfiguredChecks;
}

public sealed record AlertEvent(
    string EventId,
    string IncidentId,
    string RuleId,
    int RuleRevision,
    AlertKind Kind,
    AlertEventPhase Phase,
    string ScopeId,
    string NetworkId,
    long ObservedUtcMs,
    double? ObservedMetric,
    string EvidenceDescription,
    bool SuppressPopup);

/// <summary>CAS version is changed by the durable store in the same transaction as the event.</summary>
public sealed record AlertIncidentState(
    string Key,
    long Version,
    string? ActiveIncidentId,
    int ActiveRuleRevision,
    long LastTriggerUtcMs,
    long NextIncidentNumber);

public static class AlertIdentity
{
    private static string Part(string value) => $"{value.Length}:{value}";

    public static string StateKey(AlertRule rule) =>
        Part(rule.RuleId) + Part(rule.ScopeId) + Part(rule.NetworkId);

    public static string QuotaEventId(QuotaThresholdEvent source) =>
        "quota:" + Part(source.RuleId) + Part(source.RuleRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
        Part(source.CycleIdentifier) + Part(source.ThresholdBps.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
