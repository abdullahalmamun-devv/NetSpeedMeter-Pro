namespace SpeedMeter.Core;

public enum ScopeMode { AutoPrimary, SelectedAdapter, ValidatedAggregate, RawDiagnostic }

public interface IRouteProvider
{
    IReadOnlyList<AdapterScopeCandidate> GetCandidates();
}

public enum TopologyState
{
    SinglePrimary,
    DualStackDisjoint,
    AmbiguousMultipleDefaults,
    VpnTunnelActive,
    NoActiveEgress,
    SelectedAdapterMissing,
    UnverifiedTopology
}

public enum InterfaceClassification { PhysicalEthernet, PhysicalWireless, VpnTunnel, VirtualBridge, Other }

/// <summary>
/// A topology hint, not proof of independent boundaries or complete dual-stack routing.
/// RouteProvider's legacy RouteMetric is a preference rank, not a validated OS metric.
/// </summary>
public sealed record AdapterScopeCandidate(
    string Id, string Name, string Description, bool IsUp, bool IsHardware, ulong LinkSpeed,
    InterfaceClassification Classification, bool HasDefaultRoute, uint RouteMetric = uint.MaxValue)
{
    public bool HasIpv4DefaultRoute { get; init; } = HasDefaultRoute;
    public bool HasIpv6DefaultRoute { get; init; } = false;
    public uint Ipv4Metric { get; init; } = RouteMetric;
    public uint Ipv6Metric { get; init; } = uint.MaxValue;
}

/// <summary>A decision owns its membership; callers cannot mutate a published boundary.</summary>
public sealed record CanonicalScopeDecision
{
    public Guid ScopeId { get; }
    public long Revision { get; }
    public ScopeMode Mode { get; }
    public TopologyState Topology { get; }
    public IReadOnlyList<string> ActiveAdapterIds { get; }
    public string? PrimaryAdapterId { get; }
    public string Summary { get; }
    public string Detail { get; }
    public bool IsAmbiguous { get; }
    public DateTimeOffset EvaluatedAt { get; }

    public CanonicalScopeDecision(Guid scopeId, long revision, ScopeMode mode, TopologyState topology,
        IReadOnlyList<string> activeAdapterIds, string? primaryAdapterId, string summary,
        string detail, bool isAmbiguous, DateTimeOffset evaluatedAt)
    {
        ScopeId = scopeId;
        Revision = revision;
        Mode = mode;
        Topology = topology;
        ActiveAdapterIds = Array.AsReadOnly(activeAdapterIds.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
        PrimaryAdapterId = primaryAdapterId;
        Summary = summary;
        Detail = detail;
        IsAmbiguous = isAmbiguous;
        EvaluatedAt = evaluatedAt;
    }
}

/// <summary>
/// Experimental diagnostic policy. It is not connected to the collector and cannot establish
/// validated aggregate independence or dual-stack/VPN accounting from these candidate hints.
/// </summary>
public static class ScopePolicy
{
    public static CanonicalScopeDecision Evaluate(ScopeMode mode, string? selectedAdapterId,
        IReadOnlyList<AdapterScopeCandidate> candidates, CanonicalScopeDecision? previousDecision = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var up = candidates.Where(c => c.IsUp).ToArray();

        CanonicalScopeDecision Decision(TopologyState topology, IEnumerable<string> activeIds,
            string? primaryId, string summary, string detail, bool ambiguous = false)
        {
            var members = activeIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var unchanged = previousDecision is not null && previousDecision.Mode == mode &&
                previousDecision.Topology == topology && previousDecision.PrimaryAdapterId == primaryId &&
                previousDecision.IsAmbiguous == ambiguous &&
                previousDecision.ActiveAdapterIds.SequenceEqual(members, StringComparer.Ordinal);
            var revision = unchanged ? previousDecision!.Revision : checked((previousDecision?.Revision ?? 0) + 1);
            return new(previousDecision?.ScopeId ?? Guid.NewGuid(), revision, mode, topology,
                members, primaryId, summary, detail, ambiguous, DateTimeOffset.UtcNow);
        }

        switch (mode)
        {
            case ScopeMode.SelectedAdapter:
            {
                if (string.IsNullOrEmpty(selectedAdapterId))
                    return Decision(TopologyState.SelectedAdapterMissing, [], null,
                        "No adapter selected", "Choose an adapter from the interface inventory.");

                var matches = candidates.Where(c => c.Id == selectedAdapterId).ToArray();
                if (matches.Length > 1)
                    return Decision(TopologyState.UnverifiedTopology, [], selectedAdapterId,
                        "Selected adapter identity is ambiguous", "Duplicate interface identity in inventory; no boundary selected.", true);
                var match = matches.SingleOrDefault();
                if (match is null || !match.IsUp)
                    return Decision(TopologyState.SelectedAdapterMissing, [], selectedAdapterId,
                        "Selected adapter unavailable", "Waiting for the selected interface. Automatic fallback is disabled.");

                return Decision(TopologyState.SinglePrimary, [match.Id], match.Id,
                    $"Selected: {match.Name}", $"Selected interface {match.Name}; includes local network traffic.");
            }

            case ScopeMode.AutoPrimary:
            {
                if (up.Length == 0)
                    return Decision(TopologyState.NoActiveEgress, [], null,
                        "No connected interface observed", "No up interface was supplied by the inventory.");

                // Name/type heuristics do not prove tunnel coverage, independence, or physical egress.
                if (up.Any(c => !c.IsHardware || c.Classification is not
                        (InterfaceClassification.PhysicalEthernet or InterfaceClassification.PhysicalWireless)) ||
                    up.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != up.Length)
                    return Decision(TopologyState.UnverifiedTopology, [], null,
                        "Primary boundary is unverified",
                        "Tunnel, virtual, unknown or duplicate interface hints require explicit boundary selection. No automatic usage boundary chosen.", true);

                var defaults = up.Where(c => c.HasDefaultRoute).ToArray();
                if (defaults.Length == 0)
                    return Decision(TopologyState.NoActiveEgress, [], null,
                        "No primary route candidate", "Connected interfaces do not establish a default route. Select an interface explicitly.", true);

                // Dual-stack disjoint check: if different physical interfaces have IPv4 vs IPv6 default routes
                var v4Defaults = up.Where(c => c.HasIpv4DefaultRoute).ToArray();
                var v6Defaults = up.Where(c => c.HasIpv6DefaultRoute).ToArray();
                if (v4Defaults.Length == 1 && v6Defaults.Length == 1 && v4Defaults[0].Id != v6Defaults[0].Id)
                {
                    return Decision(TopologyState.DualStackDisjoint, [], null,
                        "Dual-stack disjoint egress",
                        "IPv4 and IPv6 routes point to different physical interfaces. A single boundary cannot represent both without double-counting; select an interface explicitly.", true);
                }

                if (defaults.Length > 1)
                    return Decision(TopologyState.AmbiguousMultipleDefaults, [], null,
                        "Multiple primary route candidates",
                        "Route family and independent egress coverage are unverified. Preference ranks cannot establish a canonical boundary; select an interface explicitly.", true);

                var primary = defaults[0];
                return Decision(TopologyState.SinglePrimary, [primary.Id], primary.Id,
                    $"Primary candidate: {primary.Name}",
                    "Single physical route candidate in the supplied inventory.");
            }

            case ScopeMode.ValidatedAggregate:
                // No independence-evidence contract exists yet. Hardware labels alone are insufficient.
                return Decision(TopologyState.UnverifiedTopology, [], null,
                    "Validated aggregation unavailable",
                    "Explicit membership and independent-boundary evidence are not implemented. No interfaces are eligible for validated aggregate accounting.", true);

            case ScopeMode.RawDiagnostic:
                return Decision(up.Length == 0 ? TopologyState.NoActiveEgress : TopologyState.UnverifiedTopology,
                    up.Select(c => c.Id), null, $"Diagnostic ({up.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count()} active)",
                    "Raw interface inspection only. Membership does not establish independence and is ineligible for deduplicated quota accounting.", true);

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}

/// <summary>
/// Stabilizes canonical scope decisions against rapid route flapping.
/// Bounces occurring within debounceSeconds are filtered, preventing excessive revision increments and notification storms.
/// </summary>
public sealed class ScopeStabilizer
{
    private readonly double debounceSeconds;
    private CanonicalScopeDecision currentDecision;
    private CanonicalScopeDecision? candidateDecision;
    private double candidateFirstObservedMonotonic;

    public ScopeStabilizer(CanonicalScopeDecision initialDecision, double debounceSeconds = 3.0)
    {
        currentDecision = initialDecision ?? throw new ArgumentNullException(nameof(initialDecision));
        this.debounceSeconds = Math.Max(0, debounceSeconds);
    }

    public CanonicalScopeDecision Current => currentDecision;
    public CanonicalScopeDecision? PendingCandidate => candidateDecision;

    public CanonicalScopeDecision Update(CanonicalScopeDecision evaluated, double monotonicSeconds)
    {
        ArgumentNullException.ThrowIfNull(evaluated);

        // Functionally identical to current decision: clear any pending bouncing candidate
        if (evaluated.Mode == currentDecision.Mode &&
            evaluated.Topology == currentDecision.Topology &&
            evaluated.PrimaryAdapterId == currentDecision.PrimaryAdapterId &&
            evaluated.IsAmbiguous == currentDecision.IsAmbiguous &&
            evaluated.ActiveAdapterIds.SequenceEqual(currentDecision.ActiveAdapterIds, StringComparer.Ordinal))
        {
            candidateDecision = null;
            return currentDecision;
        }

        // Fast-path immediate drop: missing selected adapter, total loss of egress, or debounce disabled (0)
        if (evaluated.Topology is TopologyState.SelectedAdapterMissing or TopologyState.NoActiveEgress || debounceSeconds == 0)
        {
            candidateDecision = null;
            currentDecision = evaluated;
            return currentDecision;
        }

        // Different candidate observed: start or restart debounce timer
        if (candidateDecision is null ||
            candidateDecision.PrimaryAdapterId != evaluated.PrimaryAdapterId ||
            candidateDecision.Topology != evaluated.Topology ||
            candidateDecision.Mode != evaluated.Mode)
        {
            candidateDecision = evaluated;
            candidateFirstObservedMonotonic = monotonicSeconds;
            return currentDecision;
        }

        // Candidate persisted long enough to commit
        if (monotonicSeconds - candidateFirstObservedMonotonic >= debounceSeconds)
        {
            currentDecision = candidateDecision;
            candidateDecision = null;
            return currentDecision;
        }

        return currentDecision;
    }
}
