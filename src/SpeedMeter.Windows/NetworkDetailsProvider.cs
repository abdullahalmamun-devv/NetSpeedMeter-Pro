using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SpeedMeter.Core;

namespace SpeedMeter.Windows;

/// <summary>Reads one explicitly selected interface. It never probes the public network.</summary>
public sealed unsafe class NetworkDetailsProvider : INetworkDetailsProvider
{
    public NetworkDetailsSnapshot Read(string? selectedAdapterId, string? scopeId)
    {
        var observed = DateTimeOffset.UtcNow;
        if (!OperatingSystem.IsWindows())
            return Unavailable(selectedAdapterId, scopeId, observed, ProviderAvailability.Unsupported, "Windows is required.");
        if (string.IsNullOrWhiteSpace(selectedAdapterId))
            return Unavailable(selectedAdapterId, scopeId, observed, ProviderAvailability.TemporarilyUnavailable, "Select an adapter to view its details.");

        try
        {
            var matches = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => string.Equals(CanonicalId(n.Id), CanonicalId(selectedAdapterId), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                return Unavailable(selectedAdapterId, scopeId, observed, ProviderAvailability.TemporarilyUnavailable,
                    matches.Length == 0 ? "Selected adapter is unavailable." : "Selected adapter identity is ambiguous.");

            var nic = matches[0];
            var properties = nic.GetIPProperties();
            var ipv4 = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => new NetworkAddressDetail(a.Address.ToString(), SafePrefix(a))).ToArray();
            var ipv6 = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(a => new NetworkAddressDetail(a.Address.ToString(), SafePrefix(a))).ToArray();
            var gateways = properties.GatewayAddresses.Select(g => g.Address?.ToString()).OfType<string>().ToArray();
            var dns = properties.DnsAddresses.Select(a => a.ToString()).ToArray();
            bool? dhcp = null;
            try { dhcp = properties.GetIPv4Properties()?.IsDhcpEnabled; }
            catch (NetworkInformationException) { }
            catch (NotSupportedException) { }

            var native = ReadNative(nic.Id);
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            var isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            var (wifiSsid, wifiSignal, wifiBand, wifiChannel, wifiAuth, wifiLimitation) = isWifi
                ? ReadWifiDetails(nic.Id)
                : ((string?)null, (int?)null, (string?)null, (int?)null, (string?)null, "Wi-Fi fields do not apply to this interface.");

            return new NetworkDetailsSnapshot(
                CanonicalId(nic.Id), scopeId, observed, ProviderAvailability.Available,
                nic.Name, nic.NetworkInterfaceType.ToString(), nic.OperationalStatus.ToString(),
                ipv4, ipv6, gateways, dns, dhcp,
                native.NetworkGuid is Guid profile && profile != Guid.Empty ? profile.ToString("D") : null,
                null, // An OS profile GUID is an identity, not a friendly profile name.
                mac.Length > 0 ? string.Join(":", mac.Select(b => b.ToString("X2"))) : null,
                native.ReceiveBitsPerSecond, native.TransmitBitsPerSecond,
                wifiSsid, wifiSignal, wifiBand, wifiChannel, wifiAuth, wifiLimitation,
                native.Diagnostic);
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException or PlatformNotSupportedException or InvalidOperationException)
        {
            return Unavailable(selectedAdapterId, scopeId, observed, ProviderAvailability.TemporarilyUnavailable,
                $"Network details unavailable ({ex.GetType().Name}).");
        }
    }

    private static int? SafePrefix(UnicastIPAddressInformation address)
    {
        try { return address.PrefixLength; }
        catch (NetworkInformationException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static string CanonicalId(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("D") : id;

    private static NetworkDetailsSnapshot Unavailable(string? adapter, string? scope, DateTimeOffset at,
        ProviderAvailability availability, string reason) => new(adapter, scope, at, availability,
        null, null, null, [], [], [], [], null, null, null, null, null, null,
        null, null, null, null, null, "Wi-Fi details unavailable.", reason);

    private static (Guid? NetworkGuid, ulong? ReceiveBitsPerSecond, ulong? TransmitBitsPerSecond, string Diagnostic) ReadNative(string id)
    {
        if (!Environment.Is64BitProcess || sizeof(InterfaceRow) != 1352)
            return (null, null, null, "Directional link speed and OS profile identity unavailable on this architecture.");
        if (!Guid.TryParse(id, out var adapterGuid))
            return (null, null, null, "Adapter GUID unavailable for native link details.");
        var error = GetIfTable2(out var table);
        if (error != 0 || table == 0)
            return (null, null, null, $"Native link details unavailable (GetIfTable2 code {error}).");
        try
        {
            var count = *(uint*)table;
            var rows = (InterfaceRow*)((byte*)table + 8);
            for (var index = 0; index < count; index++)
            {
                var row = rows + index;
                if (row->Guid != adapterGuid) continue;
                return (row->NetworkGuid == Guid.Empty ? null : row->NetworkGuid,
                    row->ReceiveLinkSpeed == 0 ? null : row->ReceiveLinkSpeed,
                    row->TransmitLinkSpeed == 0 ? null : row->TransmitLinkSpeed,
                    "Directional values are interface link speed, not internet speed.");
            }
            return (null, null, null, "Selected interface has no native link row.");
        }
        finally { FreeMibTable(table); }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfTable2(out nint table);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeMibTable(nint table);

    private static (string? Ssid, int? Signal, string? Band, int? Channel, string? Auth, string Limitation) ReadWifiDetails(string nicId)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(startInfo);
            if (proc is null) return (null, null, null, null, null, "WLAN query unavailable.");
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            string? ssid = null;
            int? signal = null;
            string? band = null;
            int? channel = null;
            string? auth = null;

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2) continue;
                var key = parts[0].Trim();
                var val = parts[1].Trim();

                if (key.Equals("SSID", StringComparison.OrdinalIgnoreCase)) ssid = val;
                else if (key.Equals("Signal", StringComparison.OrdinalIgnoreCase))
                {
                    var numStr = val.TrimEnd('%').Trim();
                    if (int.TryParse(numStr, out var s)) signal = s;
                }
                else if (key.Equals("Channel", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(val, out var ch))
                    {
                        channel = ch;
                        band = ch <= 14 ? "2.4 GHz" : (ch <= 177 ? "5 GHz" : "6 GHz");
                    }
                }
                else if (key.Equals("Radio type", StringComparison.OrdinalIgnoreCase))
                {
                    if (band is null)
                    {
                        if (val.Contains("802.11a") || val.Contains("802.11ac") || val.Contains("802.11ax") || val.Contains("802.11be")) band = "5 GHz";
                        else if (val.Contains("802.11b") || val.Contains("802.11g")) band = "2.4 GHz";
                    }
                }
                else if (key.Equals("Authentication", StringComparison.OrdinalIgnoreCase)) auth = val;
            }

            if (ssid is not null)
            {
                return (ssid, signal, band, channel, auth, "Wi-Fi connection metadata observed from Windows WLAN interface.");
            }

            return (null, null, null, null, null, "Wi-Fi interface not currently associated with an access point.");
        }
        catch (Exception ex)
        {
            return (null, null, null, null, null, $"Wi-Fi metadata query failed ({ex.GetType().Name}).");
        }
    }

    // Matches the validated x64 MIB_IF_ROW2 layout already used by AdapterProvider.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceRow
    {
        public ulong Luid;
        public uint Index;
        public Guid Guid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        public ulong InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos;
        public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors;
        public ulong OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }
}
