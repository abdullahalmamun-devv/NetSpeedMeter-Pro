namespace SpeedMeter.Core;

/// <summary>Connectivity is independent of collector and connection-quality state.</summary>
public enum ConnectivityState { Unknown, Checking, Reachable, Limited, NoLocalConnection, SignInSuspected }
public enum LocalLinkState { Unknown, Connected, Disconnected }
public enum ProbeProtocol { Https, Icmp }
public enum ProbeOutcome
{
    Success, Timeout, DnsFailure, ProxyFailure, ProxyAuthenticationRequired,
    TlsFailure, UnexpectedResponse, PortalRedirect, IcmpBlocked, Unsupported, Canceled, TransportFailure
}

/// <summary>An endpoint is supplied only after ownership and intended automated use are approved.</summary>
public sealed record ConnectivityEndpoint
{
    public string Id { get; }
    public string Label { get; }
    public ProbeProtocol Protocol { get; }
    public string Target { get; }
    public int? ExpectedHttpStatusCode { get; }
    public TimeSpan Timeout { get; }
    public TimeSpan MinimumInterval { get; }
    public string OwnershipPolicy { get; }

    public ConnectivityEndpoint(string id, string label, ProbeProtocol protocol, string target,
        int? expectedHttpStatusCode, TimeSpan timeout, TimeSpan minimumInterval, string ownershipPolicy)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) ||
            string.IsNullOrWhiteSpace(ownershipPolicy))
            throw new ArgumentException("Endpoint identity, visible label, and approved usage policy are required.");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(3))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Probe timeout must be positive and at most 3 seconds.");
        if (minimumInterval < TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(minimumInterval), "Background probes must be at least 30 seconds apart.");
        if (protocol == ProbeProtocol.Https)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
                expectedHttpStatusCode is < 100 or > 599 or null)
                throw new ArgumentException("HTTPS endpoint requires a secure absolute URL and expected HTTP status.", nameof(target));
            Target = uri.AbsoluteUri;
        }
        else if (protocol == ProbeProtocol.Icmp)
        {
            if (string.IsNullOrWhiteSpace(target) || Uri.CheckHostName(target) == UriHostNameType.Unknown ||
                expectedHttpStatusCode.HasValue)
                throw new ArgumentException("ICMP endpoint requires a host and no HTTP status.", nameof(target));
            Target = target;
        }
        else throw new ArgumentOutOfRangeException(nameof(protocol));
        Id = id;
        Label = label;
        Protocol = protocol;
        ExpectedHttpStatusCode = expectedHttpStatusCode;
        Timeout = timeout;
        MinimumInterval = minimumInterval;
        OwnershipPolicy = ownershipPolicy;
    }
}

public sealed class ConnectivityEndpointRegistry
{
    public IReadOnlyList<ConnectivityEndpoint> Endpoints { get; }
    public ConnectivityEndpointRegistry(IEnumerable<ConnectivityEndpoint> endpoints)
    {
        var copy = endpoints?.ToArray() ?? throw new ArgumentNullException(nameof(endpoints));
        if (copy.Any(x => x is null) || copy.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Endpoints must have unique IDs.", nameof(endpoints));
        if (copy.Length > 0 && !copy.Any(x => x.Protocol == ProbeProtocol.Https))
            throw new ArgumentException("At least one configured reachability path must use HTTPS.", nameof(endpoints));
        Endpoints = Array.AsReadOnly(copy);
    }
}

public sealed record ProbeObservation(string EndpointId, ProbeOutcome Outcome,
    DateTimeOffset ObservedAtUtc, double MonotonicSeconds, string? Detail = null, int? HttpStatusCode = null);

public sealed record ConnectivityIncident(string ScopeId, DateTimeOffset FirstFailedObservationUtc,
    DateTimeOffset ConfirmationUtc, DateTimeOffset? FirstSuccessfulObservationUtc,
    DateTimeOffset? FinalizationUtc, bool EndUncertain, TimeSpan TimingResolution,
    string EvidenceType);

public sealed record ConnectivitySnapshot(ConnectivityState State, LocalLinkState LocalLink,
    string ScopeId, bool RemoteChecksEnabled, bool RemoteChecksSuppressedByMetering,
    DateTimeOffset? LastCheckUtc, DateTimeOffset? LastSuccessfulCheckUtc,
    IReadOnlyList<ProbeObservation> LastEvidence, ConnectivityIncident? ActiveIncident,
    IReadOnlyList<ConnectivityIncident> CompletedIncidents, double ReachableObservedSeconds,
    double UnavailableObservedSeconds, double UnknownObservedSeconds,
    double LocalConnectedObservedSeconds, double LocalDisconnectedObservedSeconds)
{
    public double? ObservedAvailability => ReachableObservedSeconds + UnavailableObservedSeconds > 0
        ? ReachableObservedSeconds / (ReachableObservedSeconds + UnavailableObservedSeconds) : null;
    public string AvailabilityLabel => RemoteChecksEnabled && !RemoteChecksSuppressedByMetering
        ? "Observed remote reachability" : "Local link only";
    public double? LocalLinkAvailability => LocalConnectedObservedSeconds + LocalDisconnectedObservedSeconds > 0
        ? LocalConnectedObservedSeconds / (LocalConnectedObservedSeconds + LocalDisconnectedObservedSeconds) : null;
}

public sealed record ConnectivityAvailability(double? ObservedAvailability, double MeasuredCoverage);

public static class ConnectivityAvailabilityCalculator
{
    public static ConnectivityAvailability Calculate(double reachableSeconds, double unavailableSeconds,
        double selectedRangeSeconds)
    {
        if (!double.IsFinite(reachableSeconds) || !double.IsFinite(unavailableSeconds) ||
            !double.IsFinite(selectedRangeSeconds) || reachableSeconds < 0 || unavailableSeconds < 0 ||
            selectedRangeSeconds <= 0 || reachableSeconds + unavailableSeconds > selectedRangeSeconds + 0.000001)
            throw new ArgumentOutOfRangeException(nameof(selectedRangeSeconds));
        var measured = reachableSeconds + unavailableSeconds;
        return new ConnectivityAvailability(measured > 0 ? reachableSeconds / measured : null,
            measured / selectedRangeSeconds);
    }
}

/// <summary>Single-threaded evidence reducer. All elapsed durations use monotonic timestamps.</summary>
public sealed class ConnectivityTracker
{
    private readonly ConnectivityEndpointRegistry registry;
    private readonly TimeSpan staleAfter;
    private readonly List<ConnectivityIncident> completed = [];
    private ProbeObservation[] lastEvidence = [];
    private string scopeId = "";
    private LocalLinkState link = LocalLinkState.Unknown;
    private ConnectivityState state = ConnectivityState.Unknown;
    private bool enabled;
    private bool meteredSuppressed;
    private int failures;
    private int successes;
    private double? firstFailureMono;
    private DateTimeOffset? firstFailureUtc;
    private DateTimeOffset? firstSuccessUtc;
    private double? firstSuccessMono;
    private double? stateAnchorMono;
    private double? lastObservationMono;
    private DateTimeOffset? lastCheckUtc;
    private DateTimeOffset? lastSuccessUtc;
    private ConnectivityIncident? active;
    private double reachableSeconds;
    private double unavailableSeconds;
    private double unknownSeconds;
    private double localConnectedSeconds;
    private double localDisconnectedSeconds;
    private double? localAnchorMono;

    public ConnectivityTracker(ConnectivityEndpointRegistry registry, TimeSpan? staleAfter = null)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.staleAfter = staleAfter ?? TimeSpan.FromSeconds(90);
        if (this.staleAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
    }

    public ConnectivitySnapshot Snapshot => new(state, link, scopeId, enabled, meteredSuppressed,
        lastCheckUtc, lastSuccessUtc, Array.AsReadOnly((ProbeObservation[])lastEvidence.Clone()), active,
        Array.AsReadOnly(completed.ToArray()),
        reachableSeconds + (state == ConnectivityState.Reachable && stateAnchorMono.HasValue &&
            lastObservationMono.HasValue ? Math.Max(0, lastObservationMono.Value - stateAnchorMono.Value) : 0),
        unavailableSeconds + (active is not null && state == ConnectivityState.Limited && stateAnchorMono.HasValue &&
            lastObservationMono.HasValue ? Math.Max(0, lastObservationMono.Value - stateAnchorMono.Value) : 0),
        unknownSeconds, localConnectedSeconds, localDisconnectedSeconds);

    public void SetContext(string newScopeId, LocalLinkState newLink, bool remoteEnabled,
        bool suppressOnMetered, DateTimeOffset utc, double mono)
    {
        ValidateTime(mono);
        if (string.IsNullOrWhiteSpace(newScopeId)) throw new ArgumentException("Scope is required.", nameof(newScopeId));
        AccumulateLocal(mono);
        var effectiveEnabled = remoteEnabled && !suppressOnMetered && registry.Endpoints.Count > 0;
        if (scopeId != newScopeId || enabled != effectiveEnabled || meteredSuppressed != suppressOnMetered ||
            link != newLink)
        {
            EndObservation(lastCheckUtc ?? utc, lastObservationMono ?? mono, true);
            scopeId = newScopeId;
            link = newLink;
            enabled = effectiveEnabled;
            meteredSuppressed = suppressOnMetered;
            failures = successes = 0;
            firstFailureMono = firstSuccessMono = null;
            firstFailureUtc = firstSuccessUtc = null;
            lastCheckUtc = lastSuccessUtc = null;
            lastEvidence = [];
            state = link == LocalLinkState.Disconnected ? ConnectivityState.NoLocalConnection :
                enabled && link == LocalLinkState.Connected ? ConnectivityState.Checking : ConnectivityState.Unknown;
            stateAnchorMono = mono;
        }
        localAnchorMono = mono;
    }

    public void RecordRound(IReadOnlyList<ProbeObservation> observations, DateTimeOffset utc,
        double mono, TimeSpan cadence)
    {
        ValidateTime(mono);
        if (!enabled || link != LocalLinkState.Connected || cadence <= TimeSpan.Zero) return;
        if (lastObservationMono.HasValue && mono < lastObservationMono.Value) return;
        var previousObservationMono = lastObservationMono;
        var previousCheckUtc = lastCheckUtc;
        var byId = observations.GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        var complete = observations.Count == registry.Endpoints.Count &&
            byId.Count == registry.Endpoints.Count && registry.Endpoints.All(x => byId.ContainsKey(x.Id));
        lastEvidence = observations.ToArray();
        lastCheckUtc = utc;
        lastObservationMono = mono;
        if (!complete || observations.Any(x => x.Outcome is ProbeOutcome.Unsupported or ProbeOutcome.Canceled or
                ProbeOutcome.ProxyAuthenticationRequired or ProbeOutcome.IcmpBlocked))
        {
            EndObservation(previousCheckUtc ?? utc, previousObservationMono ?? mono, true);
            state = ConnectivityState.Limited;
            return;
        }

        if (observations.Any(x => x.Outcome == ProbeOutcome.Success))
        {
            lastSuccessUtc = utc;
            failures = 0;
            firstFailureMono = null;
            firstFailureUtc = null;
            if (++successes == 1)
            {
                firstSuccessUtc = utc;
                firstSuccessMono = mono;
                if (active is not null) Accumulate(mono, ConnectivityState.Limited);
            }
            if (successes >= 2)
            {
                if (active is not null)
                {
                    completed.Add(active with { FirstSuccessfulObservationUtc = firstSuccessUtc,
                        FinalizationUtc = utc, EndUncertain = false });
                    active = null;
                }
                state = ConnectivityState.Reachable;
                stateAnchorMono = firstSuccessMono;
            }
            else state = ConnectivityState.Checking;
            return;
        }

        successes = 0;
        firstSuccessUtc = null;
        firstSuccessMono = null;
        if (observations.Any(x => x.Outcome == ProbeOutcome.PortalRedirect))
        {
            EndObservation(previousCheckUtc ?? utc, previousObservationMono ?? mono, true);
            state = ConnectivityState.SignInSuspected;
            return;
        }
        var hardFailures = observations.All(x => x.Outcome is ProbeOutcome.Timeout or ProbeOutcome.DnsFailure or
            ProbeOutcome.ProxyFailure or ProbeOutcome.TlsFailure or ProbeOutcome.UnexpectedResponse or
            ProbeOutcome.TransportFailure);
        if (!hardFailures)
        {
            EndObservation(previousCheckUtc ?? utc, previousObservationMono ?? mono, true);
            state = ConnectivityState.Limited;
            return;
        }
        if (failures == 0)
        {
            Accumulate(mono, ConnectivityState.Limited);
            firstFailureMono = mono;
            firstFailureUtc = utc;
        }
        failures++;
        if (failures >= 3)
        {
            if (active is null)
            {
                active = new ConnectivityIncident(scopeId, firstFailureUtc!.Value, utc, null, null,
                    false, cadence, "all_configured_viable_checks_failed");
                unavailableSeconds += Math.Max(0, mono - firstFailureMono!.Value);
            }
            else Accumulate(mono, ConnectivityState.Limited);
            stateAnchorMono = mono;
        }
        state = ConnectivityState.Limited;
    }

    public void Expire(DateTimeOffset utc, double mono)
    {
        ValidateTime(mono);
        if (lastObservationMono.HasValue && mono - lastObservationMono.Value > staleAfter.TotalSeconds)
        {
            EndObservation(lastCheckUtc ?? utc, lastObservationMono.Value, true);
            failures = successes = 0;
            firstFailureMono = firstSuccessMono = null;
            firstFailureUtc = firstSuccessUtc = null;
            state = link == LocalLinkState.Disconnected ? ConnectivityState.NoLocalConnection : ConnectivityState.Unknown;
        }
    }

    public void MarkObservationGap(DateTimeOffset utc, double mono)
    {
        ValidateTime(mono);
        AccumulateLocal(lastObservationMono ?? mono);
        localAnchorMono = null;
        EndObservation(lastCheckUtc ?? utc, lastObservationMono ?? mono, true);
        failures = successes = 0;
        firstFailureMono = firstSuccessMono = null;
        firstFailureUtc = firstSuccessUtc = null;
        lastEvidence = [];
        lastCheckUtc = lastSuccessUtc = null;
        lastObservationMono = null;
        state = link == LocalLinkState.Disconnected ? ConnectivityState.NoLocalConnection : ConnectivityState.Unknown;
        stateAnchorMono = null;
    }

    private void EndObservation(DateTimeOffset utc, double mono, bool uncertain)
    {
        Accumulate(mono, ConnectivityState.Unknown);
        if (active is not null)
        {
            completed.Add(active with { FinalizationUtc = utc, EndUncertain = uncertain });
            active = null;
        }
        lastObservationMono = null;
        stateAnchorMono = null;
    }

    private void Accumulate(double mono, ConnectivityState next)
    {
        if (stateAnchorMono.HasValue && mono >= stateAnchorMono.Value)
        {
            var duration = mono - stateAnchorMono.Value;
            if (state == ConnectivityState.Reachable) reachableSeconds += duration;
            else if (active is not null && state == ConnectivityState.Limited) unavailableSeconds += duration;
            else unknownSeconds += duration;
        }
        stateAnchorMono = next == ConnectivityState.Unknown ? null : mono;
    }

    private static void ValidateTime(double mono)
    {
        if (!double.IsFinite(mono) || mono < 0) throw new ArgumentOutOfRangeException(nameof(mono));
    }

    private void AccumulateLocal(double mono)
    {
        if (!localAnchorMono.HasValue || mono < localAnchorMono.Value) return;
        var duration = mono - localAnchorMono.Value;
        if (link == LocalLinkState.Connected) localConnectedSeconds += duration;
        else if (link == LocalLinkState.Disconnected) localDisconnectedSeconds += duration;
    }
}
