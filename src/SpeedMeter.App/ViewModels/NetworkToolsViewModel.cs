using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SpeedMeter.Application;
using SpeedMeter.Core;

namespace SpeedMeter.App.ViewModels;

public sealed record PingAttemptItem(int AttemptNumber, string Status, string? Rtt);

public sealed class NetworkToolsViewModel : ViewModelBase
{
    private readonly INetworkToolsService toolsService;
    private readonly ConnectivityService connectivityService;
    private readonly ISettingsService settingsService;
    private readonly Collector collector;

    private CancellationTokenSource? pingCts;
    private CancellationTokenSource? dnsCts;
    private CancellationTokenSource? ipCts;

    // Connection Status State
    private string connectivityStateText = "Unknown";
    private string collectorStateText = "Starting";
    private string qualityStateText = "Unknown";
    private string lastCheckText = "Never";
    private bool remoteChecksEnabled;
    private bool meteredChecksAllowed;
    private string? incidentNotice;

    // Network Details State
    private string adapterName = "Detecting…";
    private string interfaceType = "—";
    private string operationalState = "—";
    private string ipv4Address = "—";
    private string ipv6Address = "—";
    private string defaultGateway = "—";
    private string dnsServers = "—";
    private string macAddress = "—";
    private string linkSpeedText = "—";
    private string wifiNotice = "Standard adapter counters remain complete.";
    private string wifiSsid = "—";
    private string wifiSignal = "—";
    private string wifiBand = "—";
    private string wifiChannel = "—";
    private string wifiAuth = "—";
    private bool isWifiAdapter;

    // Ping Tool State
    private string pingTarget = "1.1.1.1";
    private int pingCount = 10;
    private bool isPinging;
    private string pingSummary = "";
    private ObservableCollection<PingAttemptItem> pingAttempts = [];

    // DNS Tool State
    private string dnsTarget = "google.com";
    private string dnsRecordType = "A";
    private bool isResolvingDns;
    private string dnsResultsText = "";
    private string dnsDiagnosticText = "";

    // Public IP State
    private bool publicIpDisclosureAccepted;
    private bool isLookingUpPublicIp;
    private string publicIpText = "Not queried";
    private string publicIpQueriedAtText = "";
    private string publicIpCaveat = "";

    public string ConnectivityStateText { get => connectivityStateText; private set => SetProperty(ref connectivityStateText, value); }
    public string CollectorStateText { get => collectorStateText; private set => SetProperty(ref collectorStateText, value); }
    public string QualityStateText { get => qualityStateText; private set => SetProperty(ref qualityStateText, value); }
    public string LastCheckText { get => lastCheckText; private set => SetProperty(ref lastCheckText, value); }
    public string? IncidentNotice { get => incidentNotice; private set => SetProperty(ref incidentNotice, value); }

    public bool RemoteChecksEnabled
    {
        get => remoteChecksEnabled;
        set
        {
            if (SetProperty(ref remoteChecksEnabled, value))
            {
                var s = settingsService.CurrentSettings;
                s.ConnectionChecksEnabled = value;
                settingsService.Save(s);
                connectivityService.SetConfiguration(value, meteredChecksAllowed);
                UpdateConnectivityDisplay();
            }
        }
    }

    public bool MeteredChecksAllowed
    {
        get => meteredChecksAllowed;
        set
        {
            if (SetProperty(ref meteredChecksAllowed, value))
            {
                var s = settingsService.CurrentSettings;
                s.MeteredProbesAllowed = value;
                settingsService.Save(s);
                connectivityService.SetConfiguration(remoteChecksEnabled, value);
            }
        }
    }

    // Network Details Properties
    public string AdapterName { get => adapterName; private set => SetProperty(ref adapterName, value); }
    public string InterfaceType { get => interfaceType; private set => SetProperty(ref interfaceType, value); }
    public string OperationalState { get => operationalState; private set => SetProperty(ref operationalState, value); }
    public string Ipv4Address { get => ipv4Address; private set => SetProperty(ref ipv4Address, value); }
    public string Ipv6Address { get => ipv6Address; private set => SetProperty(ref ipv6Address, value); }
    public string DefaultGateway { get => defaultGateway; private set => SetProperty(ref defaultGateway, value); }
    public string DnsServers { get => dnsServers; private set => SetProperty(ref dnsServers, value); }
    public string MacAddress { get => macAddress; private set => SetProperty(ref macAddress, value); }
    public string LinkSpeedText { get => linkSpeedText; private set => SetProperty(ref linkSpeedText, value); }
    public string WifiNotice { get => wifiNotice; private set => SetProperty(ref wifiNotice, value); }
    public string WifiSsid { get => wifiSsid; private set => SetProperty(ref wifiSsid, value); }
    public string WifiSignal { get => wifiSignal; private set => SetProperty(ref wifiSignal, value); }
    public string WifiBand { get => wifiBand; private set => SetProperty(ref wifiBand, value); }
    public string WifiChannel { get => wifiChannel; private set => SetProperty(ref wifiChannel, value); }
    public string WifiAuth { get => wifiAuth; private set => SetProperty(ref wifiAuth, value); }
    public bool IsWifiAdapter { get => isWifiAdapter; private set => SetProperty(ref isWifiAdapter, value); }

    // Ping Tool Properties
    public string PingTarget { get => pingTarget; set => SetProperty(ref pingTarget, value); }
    public int PingCount { get => pingCount; set => SetProperty(ref pingCount, Math.Clamp(value, 1, 10)); }
    public bool IsPinging { get => isPinging; private set => SetProperty(ref isPinging, value); }
    public string PingSummary { get => pingSummary; private set => SetProperty(ref pingSummary, value); }
    public ObservableCollection<PingAttemptItem> PingAttempts { get => pingAttempts; private set => SetProperty(ref pingAttempts, value); }

    // DNS Tool Properties
    public string DnsTarget { get => dnsTarget; set => SetProperty(ref dnsTarget, value); }
    public string DnsRecordType { get => dnsRecordType; set => SetProperty(ref dnsRecordType, value); }
    public bool IsResolvingDns { get => isResolvingDns; private set => SetProperty(ref isResolvingDns, value); }
    public string DnsResultsText { get => dnsResultsText; private set => SetProperty(ref dnsResultsText, value); }
    public string DnsDiagnosticText { get => dnsDiagnosticText; private set => SetProperty(ref dnsDiagnosticText, value); }

    // Public IP Properties
    public bool PublicIpDisclosureAccepted
    {
        get => publicIpDisclosureAccepted;
        set
        {
            if (SetProperty(ref publicIpDisclosureAccepted, value))
            {
                var s = settingsService.CurrentSettings;
                s.PublicIpDisclosureAccepted = value;
                settingsService.Save(s);
            }
        }
    }
    public bool IsLookingUpPublicIp { get => isLookingUpPublicIp; private set => SetProperty(ref isLookingUpPublicIp, value); }
    public string PublicIpText { get => publicIpText; private set => SetProperty(ref publicIpText, value); }
    public string PublicIpQueriedAtText { get => publicIpQueriedAtText; private set => SetProperty(ref publicIpQueriedAtText, value); }
    public string PublicIpCaveat { get => publicIpCaveat; private set => SetProperty(ref publicIpCaveat, value); }

    // Commands
    public ICommand RefreshDetailsCommand { get; }
    public ICommand CopyDetailCommand { get; }
    public ICommand RunPingCommand { get; }
    public ICommand CancelPingCommand { get; }
    public ICommand RunDnsCommand { get; }
    public ICommand RunPublicIpCommand { get; }

    public NetworkToolsViewModel(
        INetworkToolsService toolsService,
        ConnectivityService connectivityService,
        ISettingsService settingsService,
        Collector collector)
    {
        this.toolsService = toolsService ?? throw new ArgumentNullException(nameof(toolsService));
        this.connectivityService = connectivityService ?? throw new ArgumentNullException(nameof(connectivityService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));

        var currentSettings = settingsService.CurrentSettings;
        remoteChecksEnabled = currentSettings.ConnectionChecksEnabled;
        meteredChecksAllowed = currentSettings.MeteredProbesAllowed;
        publicIpDisclosureAccepted = currentSettings.PublicIpDisclosureAccepted;

        RefreshDetailsCommand = new RelayCommand(RefreshNetworkDetails);
        CopyDetailCommand = new RelayCommand(p =>
        {
            if (p is string text && !string.IsNullOrEmpty(text) && text != "—")
            {
                try { Clipboard.SetText(text); } catch { }
            }
        });

        RunPingCommand = new RelayCommand(async () => await ExecutePingAsync());
        CancelPingCommand = new RelayCommand(() => pingCts?.Cancel());
        RunDnsCommand = new RelayCommand(async () => await ExecuteDnsAsync());
        RunPublicIpCommand = new RelayCommand(async () => await ExecutePublicIpAsync());

        RefreshNetworkDetails();
        UpdateConnectivityDisplay();
    }

    public void UpdateSnapshot(CollectorSnapshot snapshot)
    {
        CollectorStateText = snapshot.State.ToString();
        UpdateConnectivityDisplay();
    }

    private void UpdateConnectivityDisplay()
    {
        var snap = connectivityService.Snapshot;
        if (!remoteChecksEnabled)
        {
            ConnectivityStateText = "Checks Disabled (Local-First Mode)";
            QualityStateText = "Not Measured";
            IncidentNotice = "Passive monitoring only. Zero network requests sent.";
        }
        else
        {
            ConnectivityStateText = snap.State switch
            {
                ConnectivityState.Reachable => "Reachable (Internet Connected)",
                ConnectivityState.Limited => "Remote Checks Failing / Limited",
                ConnectivityState.NoLocalConnection => "No Local Network Connection",
                ConnectivityState.SignInSuspected => "Sign-In Required (Captive Portal Detected)",
                ConnectivityState.Checking => "Checking Reachability…",
                _ => "Unknown / Establishing Baseline"
            };

            QualityStateText = snap.State == ConnectivityState.Reachable ? "Optimal" : "Degraded";

            if (snap.ActiveIncident is not null)
            {
                IncidentNotice = $"Interruption detected at {snap.ActiveIncident.ConfirmationUtc:HH:mm:ss} UTC ({snap.ActiveIncident.EvidenceType})";
            }
            else
            {
                IncidentNotice = null;
            }
        }

        LastCheckText = snap.LastCheckUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "None";
    }

    public void RefreshNetworkDetails()
    {
        var activeScopeId = collector.Latest.Reading.ScopeId;
        var selectedAdapterId = collector.Latest.ScopeDecision?.PrimaryAdapterId;
        var details = toolsService.GetDetails(selectedAdapterId, activeScopeId);

        AdapterName = details.AdapterName ?? (details.AdapterId is not null ? $"Adapter {details.AdapterId}" : "AutoPrimary (Default)");
        InterfaceType = details.InterfaceType ?? "Ethernet / IEEE 802.3";
        OperationalState = details.OperationalState ?? "Up";

        Ipv4Address = details.Ipv4Addresses.Count > 0
            ? string.Join(", ", details.Ipv4Addresses.Select(a => a.PrefixLength.HasValue ? $"{a.Address}/{a.PrefixLength}" : a.Address))
            : "—";

        Ipv6Address = details.Ipv6Addresses.Count > 0
            ? string.Join(", ", details.Ipv6Addresses.Select(a => a.Address))
            : "—";

        DefaultGateway = details.Gateways.Count > 0 ? string.Join(", ", details.Gateways) : "—";
        DnsServers = details.DnsServers.Count > 0 ? string.Join(", ", details.DnsServers) : "—";
        MacAddress = details.MacAddress ?? "—";

        if (details.ReceiveLinkSpeedBitsPerSecond.HasValue && details.TransmitLinkSpeedBitsPerSecond.HasValue)
        {
            var rx = MetricFormat.Rate(details.ReceiveLinkSpeedBitsPerSecond.Value, bits: true);
            var tx = MetricFormat.Rate(details.TransmitLinkSpeedBitsPerSecond.Value, bits: true);
            LinkSpeedText = $"Rx: {rx}  ·  Tx: {tx}";
        }
        else
        {
            LinkSpeedText = "Link speed not reported by adapter driver";
        }

        WifiNotice = !string.IsNullOrEmpty(details.WifiLimitation)
            ? details.WifiLimitation
            : "Standard adapter counters remain complete.";

        IsWifiAdapter = details.InterfaceType?.Contains("Wireless", StringComparison.OrdinalIgnoreCase) == true
                     || details.InterfaceType?.Contains("80211", StringComparison.OrdinalIgnoreCase) == true
                     || details.WifiSsid is not null;
        WifiSsid = details.WifiSsid ?? "—";
        WifiSignal = details.WifiSignalPercent.HasValue ? $"{details.WifiSignalPercent.Value}%" : "—";
        WifiBand = details.WifiBand ?? "—";
        WifiChannel = details.WifiChannel.HasValue ? details.WifiChannel.Value.ToString() : "—";
        WifiAuth = details.WifiAuthentication ?? "—";
    }

    private async Task ExecutePingAsync()
    {
        if (IsPinging) return;
        IsPinging = true;
        PingSummary = "Pinging " + PingTarget + "…";
        PingAttempts.Clear();

        pingCts?.Dispose();
        pingCts = new CancellationTokenSource();

        try
        {
            var result = await toolsService.PingAsync(PingTarget, null, pingCts.Token, PingCount);
            for (var i = 0; i < result.Attempts.Count; i++)
            {
                var att = result.Attempts[i];
                var status = att.Outcome switch
                {
                    PingAttemptOutcome.Reply => "Reply",
                    PingAttemptOutcome.Timeout => "Request timed out",
                    _ => $"Error ({att.Error ?? "Local failure"})"
                };
                var rtt = att.RttMilliseconds.HasValue ? $"{att.RttMilliseconds.Value:F1} ms" : "—";
                PingAttempts.Add(new PingAttemptItem(i + 1, status, rtt));
            }

            if (result.ReplyCount > 0)
            {
                PingSummary = $"Replies: {result.ReplyCount}/{result.Attempts.Count} · Min: {result.MinimumRttMilliseconds:F1} ms · Mean: {result.MeanRttMilliseconds:F1} ms · Max: {result.MaximumRttMilliseconds:F1} ms";
            }
            else
            {
                PingSummary = result.Outcome == NetworkToolOutcome.Cancelled
                    ? "Ping job cancelled by user."
                    : $"0/{result.Attempts.Count} replies received. Remote endpoint did not respond.";
            }
        }
        catch (Exception ex)
        {
            PingSummary = $"Ping failed: {ex.Message}";
        }
        finally
        {
            IsPinging = false;
        }
    }

    private async Task ExecuteDnsAsync()
    {
        if (IsResolvingDns) return;
        IsResolvingDns = true;
        DnsResultsText = "Resolving " + DnsTarget + "…";
        DnsDiagnosticText = "";

        dnsCts?.Dispose();
        dnsCts = new CancellationTokenSource();

        try
        {
            var result = await toolsService.LookupDnsAsync(DnsTarget, DnsRecordType, null, dnsCts.Token);
            if (result.Outcome == NetworkToolOutcome.Success)
            {
                DnsResultsText = result.Answers.Count > 0
                    ? string.Join(Environment.NewLine, result.Answers)
                    : "No matching DNS records found.";
                DnsDiagnosticText = $"Resolved in {result.Duration.TotalMilliseconds:F0} ms via OS resolver.";
            }
            else
            {
                DnsResultsText = $"Lookup failed ({result.Outcome}).";
                DnsDiagnosticText = result.Diagnostic ?? "OS resolver returned an error.";
            }
        }
        catch (Exception ex)
        {
            DnsResultsText = $"Error: {ex.Message}";
        }
        finally
        {
            IsResolvingDns = false;
        }
    }

    private async Task ExecutePublicIpAsync()
    {
        if (IsLookingUpPublicIp) return;
        if (!PublicIpDisclosureAccepted)
        {
            PublicIpText = "Consent required";
            PublicIpCaveat = "Please accept the external request disclosure before performing this lookup.";
            return;
        }

        IsLookingUpPublicIp = true;
        PublicIpText = "Querying external provider…";
        PublicIpCaveat = "";

        ipCts?.Dispose();
        ipCts = new CancellationTokenSource();

        try
        {
            var provider = new PublicIpProvider(
                "Cloudflare Trace",
                new Uri("https://1.1.1.1/cdn-cgi/trace"),
                NetworkToolsService.RequiredPublicIpDisclosure);

            var req = new PublicIpLookupRequest(provider, null, DisclosureAccepted: true);
            var result = await toolsService.LookupPublicIpAsync(req, ipCts.Token);

            if (result.Outcome == NetworkToolOutcome.Success && result.Address is not null)
            {
                PublicIpText = $"{result.Address} ({result.AddressFamily})";
                PublicIpQueriedAtText = $"Queried at {result.QueriedAt.ToLocalTime():HH:mm:ss}";
                PublicIpCaveat = result.EgressContext;
            }
            else
            {
                PublicIpText = result.Address is not null ? $"{result.Address} (Cached)" : "Lookup Unavailable";
                PublicIpQueriedAtText = result.CacheTimestamp.HasValue ? $"Cached from {result.CacheTimestamp.Value.ToLocalTime():HH:mm:ss}" : "";
                PublicIpCaveat = result.Diagnostic ?? "Unable to contact external provider.";
            }
        }
        catch (Exception ex)
        {
            PublicIpText = "Lookup error";
            PublicIpCaveat = ex.Message;
        }
        finally
        {
            IsLookingUpPublicIp = false;
        }
    }
}
