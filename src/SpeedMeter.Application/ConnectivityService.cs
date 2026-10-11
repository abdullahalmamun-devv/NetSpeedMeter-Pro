using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public sealed record ProbeResult(ProbeOutcome Outcome, string? Detail = null, int? HttpStatusCode = null);

public interface IConnectivityProbeProvider
{
    Task<ProbeResult> ProbeAsync(ConnectivityEndpoint endpoint, CancellationToken cancellationToken);
}

/// <summary>Optional HTTPS checks. Construction does not send a request; no endpoints are built in.</summary>
public sealed class HttpsConnectivityProbeProvider : IConnectivityProbeProvider
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = true,
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<ProbeResult> ProbeAsync(ConnectivityEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.Protocol != ProbeProtocol.Https)
            return new ProbeResult(ProbeOutcome.Unsupported, "This provider supports HTTPS only.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Target);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code == endpoint.ExpectedHttpStatusCode)
                return new ProbeResult(ProbeOutcome.Success, null, code);
            if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                return new ProbeResult(ProbeOutcome.ProxyAuthenticationRequired, "Proxy authentication is required.", code);
            if (code is >= 300 and < 400 && response.Headers.Location is not null)
                return new ProbeResult(ProbeOutcome.PortalRedirect, "Unexpected HTTPS redirect; sign-in may be required.", code);
            return new ProbeResult(ProbeOutcome.UnexpectedResponse, $"Expected HTTP {endpoint.ExpectedHttpStatusCode}; observed {code}.", code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(ProbeOutcome.Canceled, "Probe canceled.");
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(ProbeOutcome.Timeout, "Probe timed out.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException ||
            ex.HttpRequestError.ToString() == "SecureConnectionError")
        {
            return new ProbeResult(ProbeOutcome.TlsFailure, ex.GetType().Name);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socket &&
            socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain ||
            ex.HttpRequestError.ToString() == "NameResolutionError")
        {
            return new ProbeResult(ProbeOutcome.DnsFailure, ex.GetType().Name);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError.ToString() == "ProxyTunnelError")
        {
            return new ProbeResult(ProbeOutcome.ProxyFailure, ex.GetType().Name);
        }
        catch (HttpRequestException ex)
        {
            return new ProbeResult(ProbeOutcome.TransportFailure, ex.GetType().Name);
        }
    }
}

/// <summary>Explicitly enabled, bounded round scheduler with stale-context rejection.</summary>
public sealed class ConnectivityService : IDisposable
{
    private readonly object sync = new();
    private readonly ConnectivityEndpointRegistry registry;
    private readonly IConnectivityProbeProvider provider;
    private readonly ConnectivityTracker tracker;
    private readonly Dictionary<string, Task<ProbeResult>> outstanding = new(StringComparer.Ordinal);
    private readonly TimeSpan roundInterval;
    private CancellationTokenSource? activeRound;
    private string contextScope = "";
    private LocalLinkState contextLink = LocalLinkState.Unknown;
    private bool contextMetered;
    private bool remoteEnabled;
    private bool allowMeteredChecks;
    private bool disposed;
    private long generation;
    private double nextRoundMono;

    public ConnectivityService(ConnectivityEndpointRegistry registry, IConnectivityProbeProvider provider,
        TimeSpan? staleAfter = null)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        tracker = new ConnectivityTracker(registry, staleAfter);
        roundInterval = registry.Endpoints.Count == 0 ? TimeSpan.FromSeconds(30) :
            TimeSpan.FromSeconds(Math.Max(30, registry.Endpoints.Max(x => x.MinimumInterval.TotalSeconds)));
    }

    public ConnectivitySnapshot Snapshot
    {
        get { lock (sync) return tracker.Snapshot; }
    }

    public void SetConfiguration(bool enabled, bool allowOnMeteredNetworks = false)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (remoteEnabled == enabled && allowMeteredChecks == allowOnMeteredNetworks) return;
            remoteEnabled = enabled;
            allowMeteredChecks = allowOnMeteredNetworks;
            generation++;
            activeRound?.Cancel();
            nextRoundMono = 0;
        }
    }

    public async Task<ConnectivitySnapshot> TickAsync(ClockReading clock, string scopeId,
        LocalLinkState localLink, bool isMetered, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? round = null;
        long roundGeneration = 0;
        lock (sync)
        {
            ThrowIfDisposed();
            if (contextScope != scopeId || contextLink != localLink || contextMetered != isMetered)
            {
                contextScope = scopeId;
                contextLink = localLink;
                contextMetered = isMetered;
                generation++;
                activeRound?.Cancel();
                nextRoundMono = 0;
            }
            var suppressed = isMetered && !allowMeteredChecks;
            tracker.SetContext(scopeId, localLink, remoteEnabled, suppressed,
                clock.UtcNow, clock.MonotonicSeconds);
            tracker.Expire(clock.UtcNow, clock.MonotonicSeconds);
            if (!remoteEnabled || suppressed || localLink != LocalLinkState.Connected ||
                registry.Endpoints.Count == 0 || activeRound is not null || outstanding.Count != 0 ||
                clock.MonotonicSeconds < nextRoundMono) return tracker.Snapshot;
            round = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeRound = round;
            roundGeneration = generation;
            nextRoundMono = clock.MonotonicSeconds + roundInterval.TotalSeconds;
        }

        try
        {
            var tasks = registry.Endpoints.Select(endpoint => ProbeOneAsync(endpoint, clock, round.Token)).ToArray();
            var observations = await Task.WhenAll(tasks).ConfigureAwait(false);
            lock (sync)
            {
                if (!disposed && roundGeneration == generation && !round.IsCancellationRequested)
                    tracker.RecordRound(observations, clock.UtcNow, clock.MonotonicSeconds, roundInterval);
                return tracker.Snapshot;
            }
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(activeRound, round)) activeRound = null;
            }
            round.Dispose();
        }
    }

    public void MarkObservationGap(ClockReading clock)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            generation++;
            activeRound?.Cancel();
            tracker.MarkObservationGap(clock.UtcNow, clock.MonotonicSeconds);
            nextRoundMono = 0;
        }
    }

    private async Task<ProbeObservation> ProbeOneAsync(ConnectivityEndpoint endpoint, ClockReading clock,
        CancellationToken roundToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(roundToken);
        timeout.CancelAfter(endpoint.Timeout);
        Task<ProbeResult> work;
        try { work = provider.ProbeAsync(endpoint, timeout.Token); }
        catch (Exception ex) { work = Task.FromResult(new ProbeResult(ProbeOutcome.TransportFailure, ex.GetType().Name)); }
        lock (sync) outstanding[endpoint.Id] = work;
        _ = work.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (sync)
            {
                if (outstanding.TryGetValue(endpoint.Id, out var current) && ReferenceEquals(current, completed))
                    outstanding.Remove(endpoint.Id);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        ProbeResult result;
        try { result = await work.WaitAsync(endpoint.Timeout, roundToken).ConfigureAwait(false); }
        catch (TimeoutException) { timeout.Cancel(); result = new ProbeResult(ProbeOutcome.Timeout, "Probe deadline exceeded."); }
        catch (OperationCanceledException) { result = new ProbeResult(ProbeOutcome.Canceled, "Probe canceled."); }
        catch (Exception ex) { result = new ProbeResult(ProbeOutcome.TransportFailure, ex.GetType().Name); }
        return new ProbeObservation(endpoint.Id, result.Outcome, clock.UtcNow,
            clock.MonotonicSeconds, result.Detail, result.HttpStatusCode);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            generation++;
            activeRound?.Cancel();
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ConnectivityService));
    }
}
