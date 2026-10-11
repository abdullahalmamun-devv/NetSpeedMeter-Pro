using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SpeedMeter.Core;

namespace SpeedMeter.Windows;

public sealed class RouteProvider : IRouteProvider
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetBestInterface(uint destAddr, out uint bestIfIndex);

    public IReadOnlyList<AdapterScopeCandidate> GetCandidates()
    {
        var candidates = new List<AdapterScopeCandidate>();
        uint primaryIfIndex = 0;
        var hasPrimaryIndex = false;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Destination 8.8.8.8 in network byte order: 0x08080808
                var destBytes = IPAddress.Parse("8.8.8.8").GetAddressBytes();
                var destAddr = BitConverter.ToUInt32(destBytes, 0);
                if (GetBestInterface(destAddr, out var bestIndex) == 0)
                {
                    primaryIfIndex = bestIndex;
                    hasPrimaryIndex = true;
                }
            }
            catch
            {
                // Fallback to managed route heuristics if P/Invoke is unavailable
            }
        }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var id = nic.Id;
                if (Guid.TryParse(id, out var guid)) id = guid.ToString("D");

                var isUp = nic.OperationalStatus == OperationalStatus.Up;
                var classification = Classify(nic);
                var isHardware = classification is InterfaceClassification.PhysicalEthernet or InterfaceClassification.PhysicalWireless;

                var ipProps = nic.GetIPProperties();
                var hasIpv4Gateway = ipProps.GatewayAddresses.Any(g => g.Address != null &&
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(g.Address));
                var hasIpv6Gateway = ipProps.GatewayAddresses.Any(g => g.Address != null &&
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                    !IPAddress.IPv6Any.Equals(g.Address));

                uint ifIndex = 0;
                var ipv4Props = ipProps.GetIPv4Properties();
                if (ipv4Props is not null)
                {
                    ifIndex = (uint)ipv4Props.Index;
                }

                var isBestIpv4 = (hasPrimaryIndex && ifIndex == primaryIfIndex);
                var hasIpv4DefaultRoute = hasIpv4Gateway || isBestIpv4;
                var hasIpv6DefaultRoute = hasIpv6Gateway;
                var hasDefaultRoute = hasIpv4DefaultRoute || hasIpv6DefaultRoute;
                var routeMetric = isBestIpv4 ? 10u : (hasDefaultRoute ? 20u : uint.MaxValue);

                candidates.Add(new AdapterScopeCandidate(
                    id,
                    nic.Name,
                    nic.Description,
                    isUp,
                    isHardware,
                    (ulong)Math.Max(0, nic.Speed),
                    classification,
                    hasDefaultRoute,
                    routeMetric)
                {
                    HasIpv4DefaultRoute = hasIpv4DefaultRoute,
                    HasIpv6DefaultRoute = hasIpv6DefaultRoute,
                    Ipv4Metric = isBestIpv4 ? 10u : (hasIpv4Gateway ? 20u : uint.MaxValue),
                    Ipv6Metric = hasIpv6Gateway ? 20u : uint.MaxValue
                });
            }
        }
        catch
        {
            // Standard user fallback: return any candidates collected
        }

        return candidates;
    }

    public static InterfaceClassification Classify(NetworkInterface nic)
    {
        var desc = (nic.Description ?? "").ToLowerInvariant();
        var name = (nic.Name ?? "").ToLowerInvariant();
        var type = nic.NetworkInterfaceType;

        if (desc.Contains("wireguard") || desc.Contains("wintun") || desc.Contains("tap-") ||
            desc.Contains("openvpn") || desc.Contains("tailscale") || desc.Contains("zerotier") ||
            desc.Contains("cisco anyconnect") || desc.Contains("globalprotect") || desc.Contains("fortinet") ||
            desc.Contains("vpn") || name.Contains("vpn") || type == NetworkInterfaceType.Ppp)
        {
            return InterfaceClassification.VpnTunnel;
        }

        if (desc.Contains("hyper-v") || desc.Contains("virtualbox") || desc.Contains("vmware") ||
            desc.Contains("vethernet") || desc.Contains("wsl") || desc.Contains("host-only") ||
            name.Contains("vethernet"))
        {
            return InterfaceClassification.VirtualBridge;
        }

        if (type == NetworkInterfaceType.Wireless80211)
        {
            return InterfaceClassification.PhysicalWireless;
        }

        if (type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.GigabitEthernet)
        {
            return InterfaceClassification.PhysicalEthernet;
        }

        return InterfaceClassification.Other;
    }
}
