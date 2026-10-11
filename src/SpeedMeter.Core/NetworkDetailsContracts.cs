namespace SpeedMeter.Core;

/// <summary>One observed address and its OS-reported prefix length.</summary>
public sealed record NetworkAddressDetail(string Address, int? PrefixLength);

/// <summary>Selected-interface information. Null means the provider could not observe that field.</summary>
public sealed record NetworkDetailsSnapshot(
    string? AdapterId,
    string? ScopeId,
    DateTimeOffset ObservedAt,
    ProviderAvailability Availability,
    string? AdapterName,
    string? InterfaceType,
    string? OperationalState,
    IReadOnlyList<NetworkAddressDetail> Ipv4Addresses,
    IReadOnlyList<NetworkAddressDetail> Ipv6Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool? DhcpEnabled,
    string? NetworkProfileId,
    string? NetworkProfileName,
    string? MacAddress,
    ulong? ReceiveLinkSpeedBitsPerSecond,
    ulong? TransmitLinkSpeedBitsPerSecond,
    string? WifiSsid,
    int? WifiSignalPercent,
    string? WifiBand,
    int? WifiChannel,
    string? WifiAuthentication,
    string WifiLimitation,
    string Diagnostic);

public interface INetworkDetailsProvider
{
    NetworkDetailsSnapshot Read(string? selectedAdapterId, string? scopeId);
}
