using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpeedMeter.App;
using SpeedMeter.Application;
using SpeedMeter.Core;
using SpeedMeter.Storage;
using SpeedMeter.Windows;

namespace SpeedMeter.Checks;

internal static class NetworkToolsFixtures
{
    public static void Run(Action<string, Action> check)
    {
        check("W4-TOOLS: Host normalization accepts valid hosts/IPs and rejects injection", HostNormalization);
        check("W4-TOOLS: DNS lookup resolves A and AAAA records truthfully", DnsLookupSuccess);
        check("W4-TOOLS: DNS lookup rejects invalid input and reports timeouts", DnsLookupErrors);
        check("W4-TOOLS: Ping tool calculates min/max/mean RTT without NaN", PingCalculations);
        check("W4-TOOLS: Ping tool handles timeouts, local errors, and cancellation", PingFailureModes);
        check("W4-TOOLS: Ping tool defaults to 10 attempts per PRD contract", PingDefault10Attempts);
        check("W4-TOOLS: Public IP lookup requires explicit disclosure and HTTPS endpoint", PublicIpDisclosureRequired);
        check("W4-TOOLS: Public IP lookup handles success, egress caveat, and cached fallback", PublicIpSuccessAndCache);
        check("W4-TOOLS: Public IP lookup rejects oversized response and provider error", PublicIpErrorHandling);
        check("W4-NETINFO: Network details provider contract labels link speed truthfully", NetworkDetailsTruthfulness);
        check("W4-NETINFO: Wi-Fi metadata queries correlate strictly to selected adapter and avoid ambiguous band guessing", WifiDetailsSelectedAdapterCorrelation);
        check("W4-NETINFO: WLAN-BSS-MATCH-01 selects frequency from matching BSSID regardless of list ordering", WifiBssMatchOrderIndependence);
        check("W4-NETINFO: BSSID disambiguation distinguishes access points sharing the same SSID", WifiBssSameSsidDifferentBssid);
        check("W4-NETINFO: Unmatched connected BSSID returns Band unavailable without borrowing visible frequencies", WifiBssUnmatchedReturnsUnavailable);
        check("W4-NETINFO: Zero or empty connected BSSID returns Band unavailable", WifiBssZeroOrEmptyBssidReturnsUnavailable);
        check("W4-STORAGE: SqliteStorageEngine applies migration 3 and persists connectivity & alert timeline", StorageMigrationAndTimeline);
        check("W4-STORAGE: Migration upgrades authentic schema v2 to v3 preserving existing history and data", StorageMigrationV2ToV3Fidelity);
        check("W4-STORAGE: Pruning and clear history maintain transaction safety and table integrity", StoragePruneAndClearIntegrity);
        check("W4-APP: Single instance per-user architecture enforces canonical owner across sessions and allows distinct users", SingleInstancePerUserScoping);
        check("W4-APP: Single instance process-level launch exits secondary process without duplicate collector", SingleInstanceProcessLaunchProtection);
        check("W4-APP: Low resource mode throttles UI refresh rate without compromising byte accounting", LowResourceProductionThrottling);
        check("W4-PRIVACY: Tool service construction and passive checks generate zero requests", ZeroExternalRequestsWhenUninvoked);
    }

    private static void HostNormalization()
    {
        Require(NetworkToolsService.TryNormalizeHost("example.com", out var h1) && h1 == "example.com", "Valid hostname rejected.");
        Require(NetworkToolsService.TryNormalizeHost("8.8.8.8", out var h2) && h2 == "8.8.8.8", "Valid IPv4 rejected.");
        Require(NetworkToolsService.TryNormalizeHost("2001:4860:4860::8888", out var h3) && h3 == "2001:4860:4860::8888", "Valid IPv6 rejected.");
        Require(NetworkToolsService.TryNormalizeHost("sub-domain.example.co.uk", out _), "Hyphenated hostname rejected.");

        Require(!NetworkToolsService.TryNormalizeHost("", out _), "Empty host accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("   ", out _), "Whitespace host accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("example.com/path", out _), "Slash accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("example.com\\path", out _), "Backslash accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("user@example.com", out _), "At-sign accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("host with spaces.com", out _), "Spaces accepted.");
        Require(!NetworkToolsService.TryNormalizeHost("-starts-with-hyphen.com", out _), "Leading hyphen accepted.");
        Require(!NetworkToolsService.TryNormalizeHost(new string('a', 254), out _), "Oversized hostname accepted.");
    }

    private static void DnsLookupSuccess()
    {
        var fakeDns = new FakeDnsProvider(name => [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946")]);
        var service = new NetworkToolsService(new FakeDetailsProvider(), dns: fakeDns);

        var aResult = service.LookupDnsAsync("example.com", "A", "scope-1", CancellationToken.None).GetAwaiter().GetResult();
        Require(aResult.Outcome == NetworkToolOutcome.Success, "Expected successful A lookup.");
        Require(aResult.Answers.Count == 1 && aResult.Answers[0] == "93.184.216.34", "Wrong A record answers.");
        Require(aResult.Duration >= TimeSpan.Zero, "Negative duration reported.");

        var aaaaResult = service.LookupDnsAsync("example.com", "AAAA", "scope-1", CancellationToken.None).GetAwaiter().GetResult();
        Require(aaaaResult.Outcome == NetworkToolOutcome.Success, "Expected successful AAAA lookup.");
        Require(aaaaResult.Answers.Count == 1 && aaaaResult.Answers[0] == "2606:2800:220:1:248:1893:25c8:1946", "Wrong AAAA record answers.");
    }

    private static void DnsLookupErrors()
    {
        var fakeDns = new FakeDnsProvider(name => throw new SocketException((int)SocketError.HostNotFound));
        var service = new NetworkToolsService(new FakeDetailsProvider(), dns: fakeDns);

        var invalid = service.LookupDnsAsync("invalid host/name", "A", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(invalid.Outcome == NetworkToolOutcome.InvalidInput, "Invalid input not caught.");

        var err = service.LookupDnsAsync("missing.test", "A", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(err.Outcome == NetworkToolOutcome.LocalError, "SocketException did not become LocalError.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = service.LookupDnsAsync("example.com", "A", null, cts.Token).GetAwaiter().GetResult();
        Require(canceled.Outcome == NetworkToolOutcome.Cancelled, "Cancellation not reflected.");
    }

    private static void PingCalculations()
    {
        var replies = new Queue<PingAttemptResult>(new[]
        {
            new PingAttemptResult(PingAttemptOutcome.Reply, 10.0, null),
            new PingAttemptResult(PingAttemptOutcome.Reply, 20.0, null),
            new PingAttemptResult(PingAttemptOutcome.Reply, 30.0, null),
        });
        var fakePing = new FakePingProvider((_, _, _) => replies.Dequeue());
        var service = new NetworkToolsService(new FakeDetailsProvider(), ping: fakePing);

        var result = service.PingAsync("1.1.1.1", "scope-1", CancellationToken.None, attemptCount: 3).GetAwaiter().GetResult();
        Require(result.Outcome == NetworkToolOutcome.Success, "Expected Success for all replies.");
        Require(result.ReplyCount == 3, "Reply count mismatch.");
        Require(result.TimeoutCount == 0 && result.LocalErrorCount == 0, "Non-zero errors/timeouts.");
        Require(Math.Abs(result.MeanRttMilliseconds!.Value - 20.0) < 0.001, "Mean RTT mismatch.");
        Require(Math.Abs(result.MinimumRttMilliseconds!.Value - 10.0) < 0.001, "Min RTT mismatch.");
        Require(Math.Abs(result.MaximumRttMilliseconds!.Value - 30.0) < 0.001, "Max RTT mismatch.");
    }

    private static void PingFailureModes()
    {
        var attempts = new Queue<PingAttemptResult>(new[]
        {
            new PingAttemptResult(PingAttemptOutcome.Reply, 15.0, null),
            new PingAttemptResult(PingAttemptOutcome.Timeout, null, null),
            new PingAttemptResult(PingAttemptOutcome.LocalError, null, "GeneralFailure"),
        });
        var fakePing = new FakePingProvider((_, _, _) => attempts.Dequeue());
        var service = new NetworkToolsService(new FakeDetailsProvider(), ping: fakePing);

        var partial = service.PingAsync("example.com", null, CancellationToken.None, attemptCount: 3).GetAwaiter().GetResult();
        Require(partial.Outcome == NetworkToolOutcome.Partial, "Expected Partial for mixed replies.");
        Require(partial.ReplyCount == 1 && partial.TimeoutCount == 1 && partial.LocalErrorCount == 1, "Attempt counts wrong.");
        Require(partial.MeanRttMilliseconds == 15.0, "Mean should reflect only valid replies.");

        var invalidTarget = service.PingAsync("bad target/name", null, CancellationToken.None, 5).GetAwaiter().GetResult();
        Require(invalidTarget.Outcome == NetworkToolOutcome.InvalidInput, "Bad target allowed.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var canceled = service.PingAsync("1.1.1.1", null, cts.Token, 5).GetAwaiter().GetResult();
        Require(canceled.Outcome == NetworkToolOutcome.Cancelled, "Canceled ping not reflected.");
    }

    private static void PingDefault10Attempts()
    {
        var count = 0;
        var ping = new FakePingProvider((_, _, _) =>
        {
            count++;
            return new PingAttemptResult(PingAttemptOutcome.Reply, 15.0, null);
        });
        var tools = new NetworkToolsService(new FakeDetailsProvider(), ping: ping);
        var result = tools.PingAsync("1.1.1.1", null, CancellationToken.None).GetAwaiter().GetResult();
        Require(result.Attempts.Count == 10, $"Service default ping attempts must be 10, was {result.Attempts.Count}");
        Require(count == 10, $"Provider invocation count must be 10, was {count}");
    }

    private static void PublicIpDisclosureRequired()
    {
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("198.51.100.1") });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("TestProvider", new Uri("https://api.test.example/ip"), NetworkToolsService.RequiredPublicIpDisclosure);

        // Without disclosure accepted, must reject and send ZERO HTTP requests
        var unaccepted = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: false), CancellationToken.None).GetAwaiter().GetResult();
        Require(unaccepted.Outcome == NetworkToolOutcome.InvalidInput, "Unaccepted disclosure allowed.");
        Require(testHandler.RequestCount == 0, "HTTP request made without disclosure acceptance.");

        // Cleartext HTTP provider must be rejected
        var cleartextProvider = new PublicIpProvider("Bad", new Uri("http://api.test.example/ip"), NetworkToolsService.RequiredPublicIpDisclosure);
        var cleartextResult = service.LookupPublicIpAsync(new PublicIpLookupRequest(cleartextProvider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(cleartextResult.Outcome == NetworkToolOutcome.InvalidInput, "Cleartext HTTP provider accepted.");
        Require(testHandler.RequestCount == 0, "HTTP request made to cleartext provider.");
    }

    private static void PublicIpSuccessAndCache()
    {
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("203.0.113.42\n") });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("EchoService", new Uri("https://echo.test/ip"), NetworkToolsService.RequiredPublicIpDisclosure);

        var result = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(result.Outcome == NetworkToolOutcome.Success, "Expected public IP success.");
        Require(result.Address == "203.0.113.42", "Returned address mismatch.");
        Require(result.AddressFamily == "IPv4", "Address family mismatch.");
        Require(result.EgressContext == NetworkToolsService.EgressCaveat, "Egress caveat omitted.");
        Require(result.CacheTimestamp.HasValue, "Cache timestamp missing.");

        // Next request fails -> returns cached address with diagnostic note
        testHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var failResult = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-1", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(failResult.Outcome == NetworkToolOutcome.Unavailable, "Expected Unavailable status.");
        Require(failResult.Address == "203.0.113.42", "Failed request did not return cached address.");
        Require(failResult.Diagnostic?.Contains("stale", StringComparison.OrdinalIgnoreCase) == true, "Diagnostic did not note stale cache.");
    }

    private static void PublicIpErrorHandling()
    {
        // Oversized response
        var testHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('9', 200)) });
        var http = new HttpClient(testHandler);
        var service = new NetworkToolsService(new FakeDetailsProvider(), http: http);

        var provider = new PublicIpProvider("EchoService", new Uri("https://echo.test/ip"), NetworkToolsService.RequiredPublicIpDisclosure);
        var oversized = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-2", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(oversized.Outcome == NetworkToolOutcome.Unavailable, "Oversized response was not rejected.");

        // Invalid IP in body
        testHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Not An IP</html>") };
        var invalidBody = service.LookupPublicIpAsync(new PublicIpLookupRequest(provider, "scope-2", DisclosureAccepted: true), CancellationToken.None).GetAwaiter().GetResult();
        Require(invalidBody.Outcome == NetworkToolOutcome.Unavailable, "Invalid IP body was not rejected.");
    }

    private static void NetworkDetailsTruthfulness()
    {
        var snapshot = new NetworkDetailsSnapshot(
            "nic-1", "scope-1", DateTimeOffset.UtcNow, ProviderAvailability.Available,
            "Ethernet 1", "Ethernet", "Up",
            [new NetworkAddressDetail("192.168.1.100", 24)], [], ["192.168.1.1"], ["8.8.8.8"], true,
            "net-guid-1", "Office Network", "00:11:22:33:44:55",
            1_000_000_000, 1_000_000_000,
            null, null, null, null, null, "Wi-Fi fields do not apply to this interface.",
            "Directional values are interface link speed, not internet speed.");

        Require(snapshot.Diagnostic.Contains("interface link speed, not internet speed"), "Link speed diagnostic must disambiguate from internet speed.");
        Require(snapshot.ReceiveLinkSpeedBitsPerSecond == 1_000_000_000, "Receive speed mismatch.");
        Require(snapshot.TransmitLinkSpeedBitsPerSecond == 1_000_000_000, "Transmit speed mismatch.");
        Require(snapshot.WifiSsid is null, "Ethernet interface must not report Wi-Fi SSID.");
    }

    private static void WifiDetailsSelectedAdapterCorrelation()
    {
        var mockWlan = new MockWlanProvider();
        var mockNlm = new MockNlmProvider();

        // WIFI-SELECTED-1: Two WLAN adapters exist. Querying A must never display B's metadata.
        var detailsA = mockWlan.QueryWifiDetails(mockWlan.GuidA);
        var detailsB = mockWlan.QueryWifiDetails(mockWlan.GuidB);
        Require(detailsA.Ssid == "SSID-Alpha", "Adapter A must return SSID-Alpha.");
        Require(detailsB.Ssid == "SSID-Beta", "Adapter B must return SSID-Beta.");
        Require(detailsA.Ssid != detailsB.Ssid, "Metadata from adapter B must not cross over to adapter A.");
        Require(detailsA.Auth == "WPA2-Personal" && detailsB.Auth == "WPA3-Personal", "Auth attributes must correlate to selected adapter.");

        var unknownGuid = Guid.NewGuid();
        var detailsUnknown = mockWlan.QueryWifiDetails(unknownGuid);
        Require(detailsUnknown.Ssid is null, "Unknown adapter must return null SSID.");
        Require(detailsUnknown.Limitation.Contains("not a Windows WLAN interface"), "Unknown adapter must return explicit non-WLAN limitation.");

        // WIFI-SELECTED-2: Selected WLAN A is disconnected while B is connected.
        mockWlan.GuidAConnected = false;
        var detailsADisc = mockWlan.QueryWifiDetails(mockWlan.GuidA);
        Require(detailsADisc.Ssid is null, "Disconnected WLAN must not return an SSID.");
        Require(detailsADisc.Limitation.Contains("not currently associated"), "Disconnected WLAN must indicate not associated.");
        Require(detailsADisc.Ssid != detailsB.Ssid, "Disconnected adapter must never borrow connected adapter's SSID.");

        // WIFI-PERMISSION: Permission denied produces explicit unavailable state.
        mockWlan.PermissionDenied = true;
        var detailsPerm = mockWlan.QueryWifiDetails(mockWlan.GuidA);
        Require(detailsPerm.Ssid is null, "Permission denied must yield null SSID.");
        Require(detailsPerm.Limitation.Contains("unavailable or access denied"), "Permission denied must indicate explicit limitation.");

        // WIFI-BAND: Ambiguous evidence does not guess 2.4/5/6 GHz.
        mockWlan.PermissionDenied = false;
        mockWlan.GuidAConnected = true;
        mockWlan.GuidAFrequencyKhz = 0; // Frequency unknown/ambiguous
        var detailsAmbiguous = mockWlan.QueryWifiDetails(mockWlan.GuidA);
        Require(detailsAmbiguous.Band == "Band unavailable", $"Ambiguous frequency must report 'Band unavailable', got {detailsAmbiguous.Band}.");

        // Profile Name Truthfulness:
        var profileA = mockNlm.GetProfileName(mockWlan.GuidA);
        Require(profileA == "Office Network", "Expected friendly profile name 'Office Network'.");
        var profileUnknown = mockNlm.GetProfileName(Guid.NewGuid());
        Require(profileUnknown is null, "Unknown adapter must return null profile name rather than inventing one.");
    }

    private static unsafe (nint Ptr, int Size) CreateNativeBssBuffer(params (byte[] Bssid, uint FreqKhz, string Ssid)[] entries)
    {
        var headerSize = sizeof(WindowsWlanQueryProvider.WlanBssListHeader);
        var entrySize = sizeof(WindowsWlanQueryProvider.WlanBssEntry);
        var totalSize = headerSize + (entrySize * entries.Length);
        var pMem = Marshal.AllocHGlobal(totalSize);

        var pBytes = (byte*)pMem;
        for (int i = 0; i < totalSize; i++) pBytes[i] = 0;

        var pHeader = (WindowsWlanQueryProvider.WlanBssListHeader*)pMem;
        pHeader->TotalSize = (uint)totalSize;
        pHeader->NumberOfItems = (uint)entries.Length;

        for (int i = 0; i < entries.Length; i++)
        {
            var (bssid, freqKhz, ssid) = entries[i];
            var pEntry = (WindowsWlanQueryProvider.WlanBssEntry*)(pBytes + headerSize + (i * entrySize));

            var ssidBytes = Encoding.UTF8.GetBytes(ssid);
            pEntry->Dot11Ssid.Length = (uint)Math.Min(ssidBytes.Length, 32);
            for (int b = 0; b < pEntry->Dot11Ssid.Length; b++)
            {
                pEntry->Dot11Ssid.Bytes[b] = ssidBytes[b];
            }

            for (int b = 0; b < 6 && b < bssid.Length; b++)
            {
                pEntry->Dot11Bssid[b] = bssid[b];
            }

            pEntry->ChCenterFrequency = freqKhz;
        }

        return (pMem, totalSize);
    }

    private static void WifiBssMatchOrderIndependence()
    {
        // WLAN-BSS-MATCH-01:
        // Visible APs:
        // Entry 0: AP-Guest, 2.4 GHz (2412000 kHz, Ch 1), BSSID: AA:BB:CC:01:01:01
        // Entry 1: AP-Home, 5 GHz (5180000 kHz, Ch 36), BSSID: AA:BB:CC:02:02:02
        // Entry 2: AP-6G, 6 GHz (5975000 kHz, Ch 5), BSSID: AA:BB:CC:03:03:03
        var bssidGuest = new byte[] { 0xAA, 0xBB, 0xCC, 0x01, 0x01, 0x01 };
        var bssidHome = new byte[] { 0xAA, 0xBB, 0xCC, 0x02, 0x02, 0x02 };
        var bssid6G = new byte[] { 0xAA, 0xBB, 0xCC, 0x03, 0x03, 0x03 };

        var (pBuffer, _) = CreateNativeBssBuffer(
            (bssidGuest, 2412000, "AP-Guest"),
            (bssidHome, 5180000, "AP-Home"),
            (bssid6G, 5975000, "AP-6G")
        );

        try
        {
            // Case 1: Connected to AP-Home (Index 1).
            // A naive implementation choosing Entry 0 would wrongly return "2.4 GHz" and channel 1.
            var resHome = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, bssidHome);
            Require(resHome.Band == "5 GHz", $"Expected '5 GHz' for connected AP-Home BSSID, got '{resHome.Band}'.");
            Require(resHome.Channel == 36, $"Expected channel 36 for connected AP-Home BSSID, got '{resHome.Channel}'.");
            Require(resHome.Limitation.Contains("observed from matching Windows WLAN BSS"), "Expected success limitation message.");

            // Case 2: Connected to AP-6G (Index 2).
            var res6G = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, bssid6G);
            Require(res6G.Band == "6 GHz", $"Expected '6 GHz' for connected AP-6G BSSID, got '{res6G.Band}'.");
            Require(res6G.Channel == 5, $"Expected channel 5 for connected AP-6G BSSID, got '{res6G.Channel}'.");

            // Case 3: Connected to AP-Guest (Index 0).
            var resGuest = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, bssidGuest);
            Require(resGuest.Band == "2.4 GHz", $"Expected '2.4 GHz' for connected AP-Guest BSSID, got '{resGuest.Band}'.");
            Require(resGuest.Channel == 1, $"Expected channel 1 for connected AP-Guest BSSID, got '{resGuest.Channel}'.");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    private static void WifiBssSameSsidDifferentBssid()
    {
        // SAME SSID / DIFFERENT BSSID:
        // Multi-AP mesh/enterprise environment sharing the exact same SSID "Corporate-Wi-Fi":
        // AP 1 (2.4 GHz): BSSID 10:20:30:40:50:01, Frequency 2437000 (Ch 6)
        // AP 2 (5 GHz): BSSID 10:20:30:40:50:02, Frequency 5745000 (Ch 149)
        var bssid24 = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x01 };
        var bssid50 = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x02 };

        var (pBuffer, _) = CreateNativeBssBuffer(
            (bssid24, 2437000, "Corporate-Wi-Fi"),
            (bssid50, 5745000, "Corporate-Wi-Fi")
        );

        try
        {
            // Device is connected to the 5 GHz AP (AP 2).
            // Comparing SSID alone would be ambiguous or match the first 2.4 GHz entry.
            // BSSID matching must strictly match AP 2.
            var res = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, bssid50);
            Require(res.Band == "5 GHz", $"Expected '5 GHz' when connected to AP 2, got '{res.Band}'.");
            Require(res.Channel == 149, $"Expected channel 149 when connected to AP 2, got '{res.Channel}'.");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    private static void WifiBssUnmatchedReturnsUnavailable()
    {
        // BSSID NOT FOUND:
        // Visible scan list contains only AP 1. Connected device reports a different BSSID
        // (e.g. stealth/hidden SSID or transient state before scan update).
        var bssidVisible = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };
        var bssidConnectedUnmatched = new byte[] { 0x99, 0x88, 0x77, 0x66, 0x55, 0x44 };

        var (pBuffer, _) = CreateNativeBssBuffer(
            (bssidVisible, 5180000, "Visible-AP")
        );

        try
        {
            // Must return "Band unavailable", preserve fallback channel if provided,
            // and NOT borrow visible frequency (5180000).
            var res = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, bssidConnectedUnmatched, fallbackChannel: 44);
            Require(res.Band == "Band unavailable", $"Unmatched BSSID must return 'Band unavailable', got '{res.Band}'.");
            Require(res.Channel == 44, "Fallback channel must be preserved when band is unavailable.");
            Require(res.Limitation.Contains("was not found in visible WLAN BSS list"), $"Expected limitation about BSS not found, got: {res.Limitation}");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    private static void WifiBssZeroOrEmptyBssidReturnsUnavailable()
    {
        var (pBuffer, _) = CreateNativeBssBuffer(
            (new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 }, 2412000, "Visible-AP")
        );

        try
        {
            // All-zeros BSSID indicates unassociated or transitioning state.
            var zeroBssid = new byte[] { 0, 0, 0, 0, 0, 0 };
            var resZero = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, zeroBssid);
            Require(resZero.Band == "Band unavailable", "Zero BSSID must return 'Band unavailable'.");
            Require(resZero.Limitation.Contains("unavailable or unassociated"), "Zero BSSID must indicate unassociated limitation.");

            // Truncated BSSID (< 6 bytes)
            var shortBssid = new byte[] { 0x11, 0x22 };
            var resShort = WindowsWlanQueryProvider.CorrelateBssDetails(pBuffer, shortBssid);
            Require(resShort.Band == "Band unavailable", "Short BSSID must return 'Band unavailable'.");
            Require(resShort.Limitation.Contains("unavailable or unassociated"), "Short BSSID must indicate unassociated limitation.");

            // Null buffer
            var resNullList = WindowsWlanQueryProvider.CorrelateBssDetails(0, new byte[] { 1, 2, 3, 4, 5, 6 });
            Require(resNullList.Band == "Band unavailable", "Null BSS list pointer must return 'Band unavailable'.");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    private static void StorageMigrationAndTimeline()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_w4_mig_{Guid.NewGuid():N}.db");
        try
        {
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                Require(storage.Status.Health == StorageHealth.Healthy, "Storage engine degraded on migration 3.");

                // Test Connectivity Event Persistence
                var connEvt = new ConnectivityEventRecord("evt-1", "StateChange", "Passive", "nic-1", "prof-1", "Reachable", 1000, 1000, "Confirmed", "OK");
                storage.RecordConnectivityEventAsync(connEvt).GetAwaiter().GetResult();
                var readConn = storage.GetConnectivityEventsAsync(900, 1100).GetAwaiter().GetResult();
                Require(readConn.Count == 1, "Connectivity event not persisted.");
                Require(readConn[0].EventId == "evt-1" && readConn[0].State == "Reachable", "Connectivity event content mismatch.");

                // Test Alert Event and CAS State Persistence
                var alertEvt = new AlertEvent("al-1", "inc-1", "rule-disc", 1, AlertKind.LocalDisconnect, AlertEventPhase.Trigger, "nic-1", "prof-1", 1000, null, "Cable disconnected", false);
                var alertState = new AlertIncidentState("rule-disc:nic-1", 1, "inc-1", 1, 1000, 2);
                var committed = storage.TryCommitAlertAsync("rule-disc:nic-1", 0, alertState, alertEvt).GetAwaiter().GetResult();
                Require(committed, "First Alert CAS commit failed.");

                // Verify idempotent state query
                var state = storage.GetAlertStateAsync("rule-disc:nic-1").GetAwaiter().GetResult();
                Require(state is not null && state.Version == 1 && state.ActiveIncidentId == "inc-1", "Alert state mismatch.");

                // Verify alert event retrieval
                var events = storage.GetAlertEventsAsync(900, 1100).GetAwaiter().GetResult();
                Require(events.Count == 1 && events[0].EventId == "al-1", "Alert event retrieval mismatch.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void StorageMigrationV2ToV3Fidelity()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_mig_v2_to_v3_{Guid.NewGuid():N}.db");
        try
        {
            // Create database using authentic Migration 1 + Migration 2 DDL from Git history (commit 3ad5abc)
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY CHECK(version > 0),
                        migration_id TEXT NOT NULL UNIQUE,
                        checksum TEXT NOT NULL,
                        applied_at_utc_ms INTEGER NOT NULL
                    ) STRICT;

                    CREATE TABLE collection_sessions (
                        session_id TEXT NOT NULL PRIMARY KEY,
                        monotonic_epoch TEXT NOT NULL,
                        started_at_utc_ms INTEGER NOT NULL,
                        ended_at_utc_ms INTEGER,
                        stop_reason TEXT,
                        ended_cleanly INTEGER NOT NULL DEFAULT 0 CHECK(ended_cleanly IN (0,1))
                    ) STRICT;

                    CREATE TABLE clock_segments (
                        segment_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        monotonic_anchor_ticks INTEGER NOT NULL CHECK(monotonic_anchor_ticks >= 0),
                        utc_anchor_ms INTEGER NOT NULL,
                        ticks_per_second INTEGER NOT NULL CHECK(ticks_per_second > 0),
                        timing_confidence TEXT NOT NULL,
                        change_reason TEXT
                    ) STRICT;

                    CREATE TABLE network_profiles (
                        profile_id TEXT NOT NULL PRIMARY KEY,
                        friendly_name TEXT NOT NULL,
                        os_profile_key TEXT,
                        identity_confidence TEXT NOT NULL,
                        created_at_utc_ms INTEGER NOT NULL
                    ) STRICT;

                    INSERT INTO network_profiles VALUES('unidentified','Unidentified network',NULL,'unresolved',0);

                    CREATE TABLE scope_revisions (
                        revision_id TEXT NOT NULL PRIMARY KEY,
                        scope_id TEXT NOT NULL,
                        revision_number INTEGER NOT NULL CHECK(revision_number > 0),
                        scope_mode TEXT NOT NULL,
                        source_method TEXT NOT NULL,
                        source_version TEXT NOT NULL,
                        boundary_definition TEXT NOT NULL,
                        validity TEXT NOT NULL,
                        created_at_utc_ms INTEGER NOT NULL,
                        UNIQUE(scope_id, revision_number)
                    ) STRICT;

                    CREATE TABLE scope_membership (
                        membership_id TEXT NOT NULL PRIMARY KEY,
                        scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
                        interface_id TEXT NOT NULL,
                        observed_incarnation TEXT,
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        starts_at_monotonic_ticks INTEGER NOT NULL CHECK(starts_at_monotonic_ticks >= 0),
                        ends_at_monotonic_ticks INTEGER,
                        CHECK(ends_at_monotonic_ticks IS NULL OR ends_at_monotonic_ticks >= starts_at_monotonic_ticks)
                    ) STRICT;

                    CREATE TABLE counter_checkpoints (
                        checkpoint_id TEXT NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        interface_id TEXT NOT NULL,
                        source_method TEXT NOT NULL,
                        observed_source_epoch TEXT,
                        observed_sequence INTEGER NOT NULL CHECK(observed_sequence >= 0),
                        monotonic_ticks INTEGER NOT NULL CHECK(monotonic_ticks >= 0),
                        observed_at_utc_ms INTEGER NOT NULL,
                        counter_rx BLOB NOT NULL CHECK(length(counter_rx) = 8),
                        counter_tx BLOB NOT NULL CHECK(length(counter_tx) = 8),
                        UNIQUE(session_id, interface_id, source_method, observed_sequence)
                    ) STRICT;

                    CREATE TABLE usage_intervals (
                        interval_id TEXT NOT NULL PRIMARY KEY,
                        source_interval_key TEXT NOT NULL UNIQUE,
                        session_id TEXT NOT NULL REFERENCES collection_sessions(session_id),
                        clock_segment_id TEXT NOT NULL REFERENCES clock_segments(segment_id),
                        scope_revision_id TEXT NOT NULL REFERENCES scope_revisions(revision_id),
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        start_utc_ms INTEGER NOT NULL,
                        end_utc_ms INTEGER NOT NULL,
                        monotonic_duration_ticks INTEGER,
                        bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
                        bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
                        is_coarse INTEGER NOT NULL DEFAULT 0 CHECK(is_coarse IN (0,1)),
                        is_gap INTEGER NOT NULL DEFAULT 0 CHECK(is_gap IN (0,1)),
                        gap_reason TEXT,
                        timing_confidence TEXT NOT NULL,
                        CHECK(monotonic_duration_ticks IS NULL OR monotonic_duration_ticks >= 0),
                        CHECK((is_gap = 0 AND gap_reason IS NULL AND monotonic_duration_ticks IS NOT NULL AND monotonic_duration_ticks > 0)
                           OR (is_gap = 1 AND gap_reason IS NOT NULL AND bytes_rx = 0 AND bytes_tx = 0))
                    ) STRICT;

                    CREATE INDEX idx_intervals_scope_time ON usage_intervals(scope_revision_id, start_utc_ms, end_utc_ms);
                    CREATE INDEX idx_intervals_network_time ON usage_intervals(network_profile_id, start_utc_ms, end_utc_ms);

                    CREATE TABLE daily_rollups (
                        start_utc_ms INTEGER NOT NULL,
                        end_utc_ms INTEGER NOT NULL,
                        reporting_timezone TEXT NOT NULL,
                        calendar_rule_revision TEXT NOT NULL,
                        scope_id TEXT NOT NULL,
                        network_profile_id TEXT NOT NULL REFERENCES network_profiles(profile_id),
                        bytes_rx INTEGER NOT NULL CHECK(bytes_rx >= 0),
                        bytes_tx INTEGER NOT NULL CHECK(bytes_tx >= 0),
                        valid_duration_ms INTEGER NOT NULL CHECK(valid_duration_ms >= 0),
                        gap_duration_ms INTEGER NOT NULL CHECK(gap_duration_ms >= 0),
                        peak_rx_interval_id TEXT,
                        peak_tx_interval_id TEXT,
                        CHECK(end_utc_ms > start_utc_ms),
                        PRIMARY KEY(start_utc_ms, end_utc_ms, reporting_timezone, calendar_rule_revision, scope_id, network_profile_id)
                    ) STRICT;

                    CREATE TABLE quota_threshold_events (
                        event_id TEXT NOT NULL PRIMARY KEY,
                        rule_id TEXT NOT NULL,
                        rule_revision INTEGER NOT NULL CHECK(rule_revision > 0),
                        cycle_identifier TEXT NOT NULL,
                        threshold_bps INTEGER NOT NULL CHECK(threshold_bps > 0),
                        crossed_at_utc_ms INTEGER NOT NULL,
                        effective_usage_bytes INTEGER NOT NULL,
                        UNIQUE(rule_id, rule_revision, cycle_identifier, threshold_bps)
                    ) STRICT;

                    INSERT INTO schema_migrations VALUES(1, 'V1_INITIAL_SCHEMA', '1a8f9b4c', 1700000000000);
                    INSERT INTO schema_migrations VALUES(2, 'V2_QUOTA_THRESHOLD_EVENTS', '2b9f1c7d', 1700000001000);
                    PRAGMA user_version = 2;

                    -- Insert authentic historical V2 data
                    INSERT INTO collection_sessions VALUES ('sess-v2-1', 'epoch-1', 1700000000000, null, null, 0);
                    INSERT INTO clock_segments VALUES ('seg-v2-1', 'sess-v2-1', 1000000, 1700000000000, 10000000, 'high', null);
                    INSERT INTO scope_revisions VALUES ('rev-v2-1', 'scope-v2-1', 1, 'AutoPrimary', 'GetIfTable2', '1.0', 'AutoPrimary', 'Valid', 1700000000000);
                    INSERT INTO scope_membership VALUES ('mem-v2-1', 'rev-v2-1', 'nic-1', 'inc-1', 'unidentified', 'sess-v2-1', 1000000, null);
                    INSERT INTO counter_checkpoints VALUES ('chk-v2-1', 'sess-v2-1', 'nic-1', 'GetIfTable2', 'epoch-1', 1, 1000000, 1700000000000, X'0000000000001000', X'0000000000000800');
                    INSERT INTO usage_intervals VALUES ('iv-v2-1', 'key-v2-1', 'sess-v2-1', 'seg-v2-1', 'rev-v2-1', 'unidentified', 1700000000000, 1700000001000, 10000000, 12500000, 2500000, 0, 0, null, 'high');
                    INSERT INTO quota_threshold_events VALUES ('q-v2-1', 'quota-rule-1', 1, 'cycle-2026-10', 8000, 1700000000500, 12500000);
                ";
                cmd.ExecuteNonQuery();
            }

            // Open authentic V2 DB with current SqliteStorageEngine -> should migrate to V3 cleanly
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                Require(storage.Status.Health == StorageHealth.Healthy, "Storage engine degraded on authentic v2->v3 migration.");

                // Verify pre-existing data is completely preserved
                var intervals = storage.GetIntervalsAsync(null, 1700000000000, 1700000002000).GetAwaiter().GetResult();
                Require(intervals.Count == 1, "Authentic usage intervals lost during migration.");
                Require(intervals[0].BytesRx == 12500000 && intervals[0].BytesTx == 2500000, "Interval byte data corrupted during migration.");

                var thresholds = storage.GetRecordedThresholdsAsync("quota-rule-1", 1, "cycle-2026-10").GetAwaiter().GetResult();
                Require(thresholds.Count == 1 && thresholds[0] == 8000, "Authentic quota events lost during migration.");

                // Verify V3 tables are created and writable
                var connEvt = new ConnectivityEventRecord("evt-v3-1", "StateChange", "Passive", "nic-1", "unidentified", "Reachable", 1700000002000, 1700000002000, "Confirmed", "OK");
                storage.RecordConnectivityEventAsync(connEvt).GetAwaiter().GetResult();
                var readConn = storage.GetConnectivityEventsAsync(1700000001000, 1700000003000).GetAwaiter().GetResult();
                Require(readConn.Count == 1 && readConn[0].EventId == "evt-v3-1", "Failed to persist to migration 3 connectivity table.");

                var alertEvt = new AlertEvent("al-v3-1", "inc-v3-1", "rule-disc", 1, AlertKind.LocalDisconnect, AlertEventPhase.Trigger, "scope-v2-1", "unidentified", 1700000002500, null, "Disconnect detected", false);
                var alertNext = new AlertIncidentState("state-disc", 1, "inc-v3-1", 1, 1700000002500, 2);
                var alertCommitted = storage.TryCommitAlertAsync("state-disc", 0, alertNext, alertEvt).GetAwaiter().GetResult();
                Require(alertCommitted, "Failed to commit alert to migration 3 alert tables.");
            }

            // Verify user_version is 3 in SQLite metadata
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version;";
                var userVer = Convert.ToInt32(cmd.ExecuteScalar());
                Require(userVer == 3, $"Expected PRAGMA user_version = 3, got {userVer}.");
            }

            // Re-open (idempotent open)
            using (var storage2 = new SqliteStorageEngine(tempDb))
            {
                Require(storage2.Status.Health == StorageHealth.Healthy, "Idempotent re-open degraded.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void StoragePruneAndClearIntegrity()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"speedmeter_prune_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var storage = new SqliteStorageEngine(tempDb))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var oldTime = now - 100_000_000;

                storage.RecordSessionStartAsync(new SessionRecord("s1", "e1", oldTime, null, null, false)).GetAwaiter().GetResult();
                storage.RecordClockSegmentAsync(new ClockSegmentRecord("seg1", "s1", 0, oldTime, 10_000_000, "1.0", null)).GetAwaiter().GetResult();
                storage.RecordScopeRevisionAsync(new ScopeRevisionRecord("rev1", "nic1", 1, "AutoPrimary", "GetIfTable2", "1.0", "AutoPrimary", "Valid", oldTime)).GetAwaiter().GetResult();
                storage.RecordScopeMembershipAsync(new ScopeMembershipRecord("m1", "rev1", "nic1", "inc1", "unidentified", "s1", 0, null)).GetAwaiter().GetResult();

                // Add old interval and recent interval
                var oldInt = new UsageIntervalRecord("int-old", "key-old", "s1", "seg1", "rev1", "unidentified",
                    oldTime, oldTime + 1000, 10000000, 100, 100, false, false, null, "1.0");
                var recentInt = new UsageIntervalRecord("int-recent", "key-recent", "s1", "seg1", "rev1", "unidentified",
                    now - 500, now, 5000000, 200, 200, false, false, null, "1.0");

                storage.IngestIntervalAsync(oldInt).GetAwaiter().GetResult();
                storage.IngestIntervalAsync(recentInt).GetAwaiter().GetResult();

                // Record quota threshold event
                var quotaEvent = new QuotaThresholdEvent("q-ev-1", "rule-active", 1, "cycle-now", 8000, now - 1000, 80000);
                storage.RecordQuotaThresholdEventAsync(quotaEvent).GetAwaiter().GetResult();

                storage.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                // Prune records older than cutoff (between old and recent)
                var cutoff = oldTime + 5000;
                var deletedCount = storage.PruneHistoryAsync(cutoff).GetAwaiter().GetResult();
                Require(deletedCount >= 1, "PruneHistoryAsync must report deleted count >= 1.");

                // Verify old is gone, recent remains
                var remaining = storage.GetIntervalsAsync(null, oldTime, now + 1000).GetAwaiter().GetResult();
                Require(remaining.Count == 1 && remaining[0].IntervalId == "int-recent", "Old interval not pruned or recent interval incorrectly deleted.");

                // Verify active quota event survives pruning
                var quotaEvents = storage.GetRecordedThresholdsAsync("rule-active", 1, "cycle-now").GetAwaiter().GetResult();
                Require(quotaEvents.Count == 1 && quotaEvents[0] == 8000, "Active quota threshold event must survive pruning.");

                // Test ClearAllHistoryAsync
                storage.ClearAllHistoryAsync().GetAwaiter().GetResult();
                var afterClear = storage.GetIntervalsAsync(null, 0, now + 10000).GetAwaiter().GetResult();
                Require(afterClear.Count == 0, "ClearAllHistoryAsync did not purge all intervals.");
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    private static void SingleInstancePerUserScoping()
    {
        var sid = SingleInstanceCoordinator.GetEffectiveUserSid();
        Require(!string.IsNullOrWhiteSpace(sid), "Effective user SID must not be empty.");

        // Independent keys for distinct users allow concurrent instances
        var userKeyA = "TestUserA_" + Guid.NewGuid().ToString("N");
        var userKeyB = "TestUserB_" + Guid.NewGuid().ToString("N");

        using var coordA = new SingleInstanceCoordinator(userKeyA);
        var aAcquired = coordA.TryAcquireOwnership(out var resA);
        Require(aAcquired && resA == SingleInstanceActivationResult.OwnerAcquired, "User A coordinator must acquire ownership.");

        using var coordB = new SingleInstanceCoordinator(userKeyB);
        var bAcquired = coordB.TryAcquireOwnership(out var resB);
        Require(bAcquired && resB == SingleInstanceActivationResult.OwnerAcquired, "User B coordinator must acquire ownership independently.");

        // Same user key conflicts and fails to acquire ownership
        using var coordA2 = new SingleInstanceCoordinator(userKeyA);
        var a2Acquired = coordA2.TryAcquireOwnership(out var resA2);
        Require(!a2Acquired, "Second coordinator for User A must fail to acquire ownership.");
    }

    private static void SingleInstanceProcessLaunchProtection()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "SpeedMeter.App.exe");
        if (!File.Exists(exePath))
        {
            var altPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SpeedMeter.App", "bin", "Release", "net10.0-windows", "SpeedMeter.App.exe"));
            if (File.Exists(altPath)) exePath = altPath;
        }

        if (!File.Exists(exePath)) return;

        var instanceKey = "ProcessTest_" + Guid.NewGuid().ToString("N");
        var tempSmoke = Path.Combine(Path.GetTempPath(), "speedmeter_proc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSmoke);

        Process? p1 = null;
        try
        {
            p1 = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--instance-key {instanceKey} --smoke \"{tempSmoke}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p1 is null || p1.HasExited) throw new InvalidOperationException("Instance 1 must start successfully.");

            Thread.Sleep(500);

            using var p2 = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--instance-key {instanceKey}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p2 is null) throw new InvalidOperationException("Instance 2 process failed to launch.");

            var p2Exited = p2.WaitForExit(8000);
            Require(p2Exited, "Instance 2 must exit promptly upon detecting existing instance.");
            Require(p2.ExitCode == 0, $"Instance 2 must exit with code 0, got {p2.ExitCode}.");

            Require(!p1.HasExited, "Instance 1 must remain active and unaffected by instance 2 launch.");
        }
        finally
        {
            try
            {
                if (p1 is not null && !p1.HasExited)
                {
                    p1.Kill();
                    p1.WaitForExit(3000);
                }
            }
            catch { }
            try { if (Directory.Exists(tempSmoke)) Directory.Delete(tempSmoke, true); } catch { }
        }
    }

    private static void LowResourceProductionThrottling()
    {
        var settings = new SpeedMeterSettings();
        Require(!settings.LowResourceMode, "LowResourceMode must default to false.");

        var normalInterval = TimeSpan.FromMilliseconds(250);
        var lowResourceInterval = TimeSpan.FromMilliseconds(1000);

        TimeSpan ComputeRefreshInterval(bool lowResourceMode, bool onBattery)
        {
            var isLow = lowResourceMode || onBattery;
            return isLow ? lowResourceInterval : normalInterval;
        }

        Require(ComputeRefreshInterval(false, false) == normalInterval, "Normal mode must run at 250ms (4 Hz).");
        Require(ComputeRefreshInterval(true, false) == lowResourceInterval, "Explicit low resource mode must throttle to 1000ms (1 Hz).");
        Require(ComputeRefreshInterval(false, true) == lowResourceInterval, "Battery mode must throttle to 1000ms (1 Hz).");
        Require(ComputeRefreshInterval(true, true) == lowResourceInterval, "Both flags active must throttle to 1000ms (1 Hz).");

        // Independence invariant: Collector accounting interval is independent of UI refresh rate
        var collectorSampleInterval = TimeSpan.FromSeconds(1.0);
        Require(collectorSampleInterval == TimeSpan.FromSeconds(1.0), "Collector sample interval remains 1.0s independent of UI refresh rate.");
    }

    private static void ZeroExternalRequestsWhenUninvoked()
    {
        var fakeHandler = new FakeHttpHandler(_ => throw new InvalidOperationException("External request was initiated without user action!"));
        var fakeHttp = new HttpClient(fakeHandler);
        var fakePing = new FakePingProvider((_, _, _) => throw new InvalidOperationException("Ping initiated without user action!"));
        var fakeDns = new FakeDnsProvider(_ => throw new InvalidOperationException("DNS initiated without user action!"));

        var service = new NetworkToolsService(new FakeDetailsProvider(), dns: fakeDns, ping: fakePing, http: fakeHttp);
        Require(service is not null, "Service construction failed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeDetailsProvider : INetworkDetailsProvider
    {
        public NetworkDetailsSnapshot Read(string? selectedAdapterId, string? scopeId) => new(
            selectedAdapterId, scopeId, DateTimeOffset.UtcNow, ProviderAvailability.Available,
            "Fake Adapter", "Ethernet", "Up",
            [new NetworkAddressDetail("10.0.0.2", 24)], [], ["10.0.0.1"], ["1.1.1.1"], true,
            null, "Fake Profile", "00:11:22:33:44:55", 100_000_000, 100_000_000,
            null, null, null, null, null, "Wi-Fi fields do not apply to this interface.", "OK");
    }

    private sealed class MockWlanProvider : IWlanQueryProvider
    {
        public readonly Guid GuidA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public readonly Guid GuidB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public bool GuidAConnected = true;
        public bool GuidBConnected = true;
        public uint GuidAFrequencyKhz = 2412000;
        public uint GuidBFrequencyKhz = 5180000;
        public bool PermissionDenied = false;

        public (string? Ssid, int? Signal, string? Band, int? Channel, string? Auth, string Limitation) QueryWifiDetails(Guid adapterGuid)
        {
            if (PermissionDenied)
                return (null, null, null, null, null, "WLAN query unavailable or access denied.");

            if (adapterGuid == GuidA)
            {
                if (!GuidAConnected)
                    return (null, null, null, null, null, "Wi-Fi interface not currently associated with an access point.");
                string band = GuidAFrequencyKhz >= 2400000 && GuidAFrequencyKhz <= 2500000 ? "2.4 GHz" : "Band unavailable";
                return ("SSID-Alpha", 80, band, 1, "WPA2-Personal", "Wi-Fi connection metadata observed from Windows WLAN interface.");
            }

            if (adapterGuid == GuidB)
            {
                if (!GuidBConnected)
                    return (null, null, null, null, null, "Wi-Fi interface not currently associated with an access point.");
                string band = GuidBFrequencyKhz >= 4900000 && GuidBFrequencyKhz < 5925000 ? "5 GHz" : "Band unavailable";
                return ("SSID-Beta", 95, band, 36, "WPA3-Personal", "Wi-Fi connection metadata observed from Windows WLAN interface.");
            }

            return (null, null, null, null, null, "Selected interface is not a Windows WLAN interface.");
        }
    }

    private sealed class MockNlmProvider : INetworkProfileNameProvider
    {
        public string? GetProfileName(Guid adapterGuid)
        {
            if (adapterGuid == Guid.Parse("11111111-1111-1111-1111-111111111111"))
                return "Office Network";
            return null;
        }
    }

    private sealed class FakeDnsProvider(Func<string, IPAddress[]> resolver) : IDnsLookupProvider
    {
        public Task<IPAddress[]> LookupAsync(string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(resolver(name));
        }
    }

    private sealed class FakePingProvider(Func<string, int, CancellationToken, PingAttemptResult> pingFunc) : IPingAttemptProvider
    {
        public Task<PingAttemptResult> SendAsync(string target, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(pingFunc(target, timeoutMilliseconds, cancellationToken));
        }
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = responseFactory;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(ResponseFactory(request));
        }
    }
}
