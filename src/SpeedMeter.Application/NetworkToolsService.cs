using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using SpeedMeter.Core;

namespace SpeedMeter.Application;

public enum NetworkToolOutcome { Success, Partial, Timeout, LocalError, InvalidInput, Cancelled, Unavailable }
public enum PingAttemptOutcome { Reply, Timeout, LocalError }
public sealed record PingAttemptResult(PingAttemptOutcome Outcome, double? RttMilliseconds, string? Error);
public sealed record PingToolResult(string Target, string? ScopeId, DateTimeOffset StartedAt,
    NetworkToolOutcome Outcome, IReadOnlyList<PingAttemptResult> Attempts, int ReplyCount, int TimeoutCount,
    int LocalErrorCount, double? MeanRttMilliseconds, double? MinimumRttMilliseconds,
    double? MaximumRttMilliseconds, string? Diagnostic);
public sealed record DnsLookupResult(string RequestedName, string RecordType, string? ScopeId,
    DateTimeOffset StartedAt, NetworkToolOutcome Outcome, IReadOnlyList<string> Answers,
    string? Resolver, TimeSpan Duration, string? Diagnostic);
public sealed record PublicIpProvider(string Label, Uri Endpoint, string Disclosure);
public sealed record PublicIpLookupRequest(PublicIpProvider Provider, string? ScopeId, bool DisclosureAccepted);
public sealed record PublicIpLookupResult(NetworkToolOutcome Outcome, string? Address, string? AddressFamily,
    string ProviderLabel, string? ScopeId, DateTimeOffset QueriedAt, DateTimeOffset? CacheTimestamp,
    string EgressContext, string? Diagnostic);

public interface IDnsLookupProvider
{
    Task<IPAddress[]> LookupAsync(string name, CancellationToken cancellationToken);
}
public interface IPingAttemptProvider
{
    Task<PingAttemptResult> SendAsync(string target, int timeoutMilliseconds, CancellationToken cancellationToken);
}
public interface INetworkToolsService
{
    NetworkDetailsSnapshot GetDetails(string? selectedAdapterId, string? scopeId);
    Task<DnsLookupResult> LookupDnsAsync(string name, string recordType, string? scopeId, CancellationToken cancellationToken);
    Task<PingToolResult> PingAsync(string target, string? scopeId, CancellationToken cancellationToken, int attemptCount = 10);
    Task<PublicIpLookupResult> LookupPublicIpAsync(PublicIpLookupRequest request, CancellationToken cancellationToken);
}

/// <summary>User-invoked jobs only. Constructing this service performs no network request.</summary>
public sealed class NetworkToolsService : INetworkToolsService
{
    public const string RequiredPublicIpDisclosure =
        "This lookup contacts an external service, which can see the source IP used to reach it.";
    public const string EgressCaveat =
        "This is the egress used to contact this provider; it is not a complete VPN leak test.";

    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly INetworkDetailsProvider details;
    private readonly IDnsLookupProvider dns;
    private readonly IPingAttemptProvider ping;
    private readonly HttpClient http;
    private PublicIpLookupResult? lastPublicIp;

    public NetworkToolsService(INetworkDetailsProvider details, IDnsLookupProvider? dns = null,
        IPingAttemptProvider? ping = null, HttpClient? http = null)
    {
        this.details = details ?? throw new ArgumentNullException(nameof(details));
        this.dns = dns ?? new SystemDnsLookupProvider();
        this.ping = ping ?? new SystemPingAttemptProvider();
        this.http = http ?? SharedHttpClient;
    }

    public NetworkDetailsSnapshot GetDetails(string? selectedAdapterId, string? scopeId) => details.Read(selectedAdapterId, scopeId);

    public async Task<DnsLookupResult> LookupDnsAsync(string name, string recordType, string? scopeId,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        if (!TryNormalizeHost(name, out var host) || recordType is not ("A" or "AAAA"))
            return new(name, recordType, scopeId, started, NetworkToolOutcome.InvalidInput, [], null,
                timer.Elapsed, "Enter a valid hostname and A or AAAA record type.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var family = recordType == "A" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
            var answers = (await dns.LookupAsync(host, deadline.Token).ConfigureAwait(false))
                .Where(a => a.AddressFamily == family).Select(a => a.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            return new(name, recordType, scopeId, started, NetworkToolOutcome.Success, answers, null,
                timer.Elapsed, answers.Length == 0 ? "No matching answers were returned; the OS resolver did not identify its server." :
                "Resolved using the OS resolver; its server was not reported.");
        }
        catch (OperationCanceledException)
        {
            return new(name, recordType, scopeId, started,
                cancellationToken.IsCancellationRequested ? NetworkToolOutcome.Cancelled : NetworkToolOutcome.Timeout,
                [], null, timer.Elapsed, "DNS lookup did not complete.");
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or InvalidOperationException)
        {
            return new(name, recordType, scopeId, started, NetworkToolOutcome.LocalError, [], null,
                timer.Elapsed, $"Local resolver error ({ex.GetType().Name}).");
        }
    }

    public async Task<PingToolResult> PingAsync(string target, string? scopeId, CancellationToken cancellationToken,
        int attemptCount = 10)
    {
        var started = DateTimeOffset.UtcNow;
        if (!TryNormalizeHost(target, out var host) || attemptCount is < 1 or > 10)
            return new(target, scopeId, started, NetworkToolOutcome.InvalidInput, [], 0, 0, 0,
                null, null, null, "Enter a valid hostname/IP and 1 to 10 attempts.");
        var attempts = new List<PingAttemptResult>(attemptCount);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        for (var index = 0; index < attemptCount; index++)
        {
            try { attempts.Add(await ping.SendAsync(host, 1000, deadline.Token).ConfigureAwait(false)); }
            catch (OperationCanceledException)
            {
                return Summarize(cancellationToken.IsCancellationRequested ? NetworkToolOutcome.Cancelled : NetworkToolOutcome.Timeout,
                    "Ping job stopped before all attempts completed.");
            }
            catch (Exception ex) when (ex is PingException or SocketException or InvalidOperationException)
            {
                attempts.Add(new(PingAttemptOutcome.LocalError, null, ex.GetType().Name));
            }
        }
        return Summarize(attempts.All(a => a.Outcome == PingAttemptOutcome.Reply) ? NetworkToolOutcome.Success :
            attempts.Any(a => a.Outcome == PingAttemptOutcome.Reply) ? NetworkToolOutcome.Partial :
            attempts.Any(a => a.Outcome == PingAttemptOutcome.LocalError) ? NetworkToolOutcome.LocalError :
            NetworkToolOutcome.Timeout, null);

        PingToolResult Summarize(NetworkToolOutcome outcome, string? diagnostic)
        {
            var replies = attempts.Where(a => a.Outcome == PingAttemptOutcome.Reply && a.RttMilliseconds is >= 0).ToArray();
            return new(target, scopeId, started, outcome, attempts.ToArray(), replies.Length,
                attempts.Count(a => a.Outcome == PingAttemptOutcome.Timeout),
                attempts.Count(a => a.Outcome == PingAttemptOutcome.LocalError),
                replies.Length == 0 ? null : replies.Average(a => a.RttMilliseconds!.Value),
                replies.Length == 0 ? null : replies.Min(a => a.RttMilliseconds!.Value),
                replies.Length == 0 ? null : replies.Max(a => a.RttMilliseconds!.Value), diagnostic);
        }
    }

    public async Task<PublicIpLookupResult> LookupPublicIpAsync(PublicIpLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var at = DateTimeOffset.UtcNow;
        var provider = request.Provider;
        var label = provider?.Label ?? "Unconfigured";
        if (!request.DisclosureAccepted || provider is null || string.IsNullOrWhiteSpace(label) ||
            provider.Endpoint is null || provider.Endpoint.Scheme != Uri.UriSchemeHttps ||
            !provider.Endpoint.IsAbsoluteUri || !string.Equals(provider.Disclosure, RequiredPublicIpDisclosure, StringComparison.Ordinal))
            return Failure(NetworkToolOutcome.InvalidInput, "An HTTPS provider and the exact external-request disclosure are required.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await http.GetAsync(provider.Endpoint, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failure(NetworkToolOutcome.Unavailable, $"Public IP lookup unavailable (HTTP {(int)response.StatusCode}).");
            if (response.Content.Headers.ContentLength is > 128)
                return Failure(NetworkToolOutcome.Unavailable, "Public IP lookup unavailable (oversized response).");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var bytes = new byte[129];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > 128)
                return Failure(NetworkToolOutcome.Unavailable, "Public IP lookup unavailable (oversized response).");
            var value = Encoding.UTF8.GetString(bytes, 0, count).Trim();
            if (!IPAddress.TryParse(value, out var address))
                return Failure(NetworkToolOutcome.Unavailable, "Public IP lookup unavailable (invalid provider response).");
            var result = new PublicIpLookupResult(NetworkToolOutcome.Success, address.ToString(),
                address.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6", label, request.ScopeId,
                at, at, EgressCaveat, null);
            lastPublicIp = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            return Failure(cancellationToken.IsCancellationRequested ? NetworkToolOutcome.Cancelled : NetworkToolOutcome.Timeout,
                "Public IP lookup unavailable (cancelled or timed out).");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return Failure(NetworkToolOutcome.Unavailable, $"Public IP lookup unavailable ({ex.GetType().Name}).");
        }

        PublicIpLookupResult Failure(NetworkToolOutcome outcome, string diagnostic)
        {
            var cache = lastPublicIp is { } previous &&
                previous.ScopeId == request.ScopeId && previous.ProviderLabel == label ? previous : null;
            return new(outcome, cache?.Address, cache?.AddressFamily, label, request.ScopeId, at,
                cache?.CacheTimestamp, EgressCaveat,
                cache is null ? diagnostic : diagnostic + " Cached address belongs to the recorded provider/scope and may be stale.");
        }
    }

    public static bool TryNormalizeHost(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 253 || input != input.Trim() ||
            input.Any(char.IsControl) || input.Contains('/') || input.Contains('\\') || input.Contains('@') ||
            input.Contains(':') && !IPAddress.TryParse(input, out _)) return false;
        if (IPAddress.TryParse(input, out var address))
        {
            normalized = address.ToString();
            return true;
        }
        try
        {
            var ascii = new IdnMapping().GetAscii(input.TrimEnd('.'));
            if (ascii.Length > 253 || ascii.Length == 0 || ascii.Split('.').Any(label =>
                    label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
                    label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) return false;
            normalized = ascii;
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private sealed class SystemDnsLookupProvider : IDnsLookupProvider
    {
        public Task<IPAddress[]> LookupAsync(string name, CancellationToken token) => Dns.GetHostAddressesAsync(name, token);
    }
    private sealed class SystemPingAttemptProvider : IPingAttemptProvider
    {
        public async Task<PingAttemptResult> SendAsync(string target, int timeoutMilliseconds, CancellationToken token)
        {
            using var client = new Ping();
            if (!IPAddress.TryParse(target, out var address))
            {
                var addresses = await Dns.GetHostAddressesAsync(target, token).ConfigureAwait(false);
                address = addresses.FirstOrDefault();
                if (address is null) return new(PingAttemptOutcome.LocalError, null, "Could not resolve target");
            }
            var reply = await client.SendPingAsync(address, TimeSpan.FromMilliseconds(timeoutMilliseconds), [], null, token).ConfigureAwait(false);
            return reply.Status switch
            {
                IPStatus.Success => new(PingAttemptOutcome.Reply, reply.RoundtripTime, null),
                IPStatus.TimedOut => new(PingAttemptOutcome.Timeout, null, null),
                _ => new(PingAttemptOutcome.LocalError, null, reply.Status.ToString())
            };
        }
    }
}
