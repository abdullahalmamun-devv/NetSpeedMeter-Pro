using System.Collections;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using SpeedMeter.Core;

namespace SpeedMeter.Windows;

public interface IWlanQueryProvider
{
    (string? Ssid, int? Signal, string? Band, int? Channel, string? Auth, string Limitation) QueryWifiDetails(Guid adapterGuid);
}

public interface INetworkProfileNameProvider
{
    string? GetProfileName(Guid adapterGuid);
}

/// <summary>Reads one explicitly selected interface. It never probes the public network.</summary>
public sealed unsafe class NetworkDetailsProvider : INetworkDetailsProvider
{
    private readonly IWlanQueryProvider wlanProvider;
    private readonly INetworkProfileNameProvider profileNameProvider;

    public NetworkDetailsProvider()
        : this(new WindowsWlanQueryProvider(), new WindowsNetworkProfileNameProvider())
    {
    }

    public NetworkDetailsProvider(IWlanQueryProvider wlanProvider, INetworkProfileNameProvider? profileNameProvider = null)
    {
        this.wlanProvider = wlanProvider ?? throw new ArgumentNullException(nameof(wlanProvider));
        this.profileNameProvider = profileNameProvider ?? new WindowsNetworkProfileNameProvider();
    }

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

            var adapterGuid = Guid.TryParse(nic.Id, out var g) ? g : Guid.Empty;
            var native = ReadNative(nic.Id);
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            var isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

            var (wifiSsid, wifiSignal, wifiBand, wifiChannel, wifiAuth, wifiLimitation) = isWifi && adapterGuid != Guid.Empty
                ? wlanProvider.QueryWifiDetails(adapterGuid)
                : ((string?)null, (int?)null, (string?)null, (int?)null, (string?)null, isWifi ? "Wi-Fi adapter GUID unavailable." : "Wi-Fi fields do not apply to this interface.");

            var friendlyProfileName = adapterGuid != Guid.Empty ? profileNameProvider.GetProfileName(adapterGuid) : null;

            return new NetworkDetailsSnapshot(
                CanonicalId(nic.Id), scopeId, observed, ProviderAvailability.Available,
                nic.Name, nic.NetworkInterfaceType.ToString(), nic.OperationalStatus.ToString(),
                ipv4, ipv6, gateways, dns, dhcp,
                native.NetworkGuid is Guid profile && profile != Guid.Empty ? profile.ToString("D") : null,
                friendlyProfileName,
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

public sealed unsafe class WindowsWlanQueryProvider : IWlanQueryProvider
{
    private const uint WlanClientVersion = 2;
    private const int WlanIntfOpcodeCurrentConnection = 7;
    private const int WlanIntfOpcodeChannelNumber = 8;
    private const int WlanInterfaceStateConnected = 1;

    public delegate uint CustomBssFetcher(nint clientHandle, in Guid adapterGuid, out nint pBssList);
    private readonly CustomBssFetcher? customBssFetcher;

    static WindowsWlanQueryProvider()
    {
        // Assert native struct layouts and offsets conform to Windows SDK wlanapi.h definitions
        if (Marshal.SizeOf<WlanInterfaceInfo>() != 532) throw new PlatformNotSupportedException("WlanInterfaceInfo must be 532 bytes.");
        if (Marshal.SizeOf<WlanConnectionAttributes>() != 604) throw new PlatformNotSupportedException("WlanConnectionAttributes must be 604 bytes.");
        if (Marshal.OffsetOf<WlanConnectionAttributes>(nameof(WlanConnectionAttributes.AssociationAttributes)) != (nint)520) throw new PlatformNotSupportedException("AssociationAttributes offset must be 520.");
        if (Marshal.OffsetOf<WlanConnectionAttributes>(nameof(WlanConnectionAttributes.SecurityAttributes)) != (nint)588) throw new PlatformNotSupportedException("SecurityAttributes offset must be 588.");
        if (Marshal.SizeOf<WlanBssEntry>() != 360) throw new PlatformNotSupportedException("WlanBssEntry must be 360 bytes.");
        if (Marshal.OffsetOf<WlanBssEntry>(nameof(WlanBssEntry.Dot11Bssid)) != (nint)40) throw new PlatformNotSupportedException("Dot11Bssid offset must be 40.");
        if (Marshal.OffsetOf<WlanBssEntry>(nameof(WlanBssEntry.ChCenterFrequency)) != (nint)92) throw new PlatformNotSupportedException("ChCenterFrequency offset must be 92.");
    }

    public WindowsWlanQueryProvider(CustomBssFetcher? customBssFetcher = null)
    {
        this.customBssFetcher = customBssFetcher;
    }

    public (string? Ssid, int? Signal, string? Band, int? Channel, string? Auth, string Limitation) QueryWifiDetails(Guid adapterGuid)
    {
        if (!OperatingSystem.IsWindows() || adapterGuid == Guid.Empty)
            return (null, null, null, null, null, "Windows WLAN service query requires a valid adapter GUID.");

        nint clientHandle = 0;
        try
        {
            var openRes = WlanOpenHandle(WlanClientVersion, 0, out _, out clientHandle);
            if (openRes != 0 || clientHandle == 0)
                return (null, null, null, null, null, $"Windows WLAN service unavailable (code {openRes}).");

            var enumRes = WlanEnumInterfaces(clientHandle, 0, out var pIfList);
            if (enumRes != 0 || pIfList == 0)
                return (null, null, null, null, null, $"WLAN interface enumeration failed (code {enumRes}).");

            try
            {
                var pIfHeader = (WlanInterfaceInfoListHeader*)pIfList;
                var numItems = pIfHeader->NumberOfItems;
                var found = false;
                var isConnected = false;

                for (uint i = 0; i < numItems; i++)
                {
                    var pInfo = (WlanInterfaceInfo*)((byte*)pIfList + 8 + (i * (uint)sizeof(WlanInterfaceInfo)));
                    if (pInfo->InterfaceGuid == adapterGuid)
                    {
                        found = true;
                        isConnected = (pInfo->State == WlanInterfaceStateConnected);
                        break;
                    }
                }

                if (!found)
                    return (null, null, null, null, null, "Selected interface is not a Windows WLAN interface.");

                if (!isConnected)
                    return (null, null, null, null, null, "Wi-Fi interface not currently associated with an access point.");

                var queryRes = WlanQueryInterface(clientHandle, in adapterGuid, WlanIntfOpcodeCurrentConnection, 0, out var dataSize, out var pData, out _);
                if (queryRes != 0 || pData == 0)
                    return (null, null, null, null, null, $"Current WLAN connection query failed (code {queryRes}).");

                try
                {
                    if (dataSize < (uint)sizeof(WlanConnectionAttributes))
                        return (null, null, null, null, null, "WLAN connection attributes payload size smaller than expected.");

                    var pConn = (WlanConnectionAttributes*)pData;
                    ref var assoc = ref pConn->AssociationAttributes;

                    var ssidLen = assoc.Dot11Ssid.Length;
                    string? ssid = null;
                    if (ssidLen is > 0 and <= 32)
                    {
                        var ssidBytes = new byte[ssidLen];
                        fixed (byte* pBytes = assoc.Dot11Ssid.Bytes)
                        {
                            Marshal.Copy((nint)pBytes, ssidBytes, 0, (int)ssidLen);
                        }
                        ssid = Encoding.UTF8.GetString(ssidBytes);
                    }

                    int? signal = (int)assoc.SignalQuality;
                    if (signal is < 0 or > 100) signal = null;

                    var auth = MapAuthAlgorithm(pConn->SecurityAttributes.Dot11AuthAlgorithm);

                    var bssidBytes = new byte[6];
                    fixed (byte* pBssid = assoc.Dot11Bssid)
                    {
                        Marshal.Copy((nint)pBssid, bssidBytes, 0, 6);
                    }

                    // Correlate frequency, band, and channel strictly to the connected BSSID
                    var (band, channel, bssLimitation) = ReadFrequencyAndChannel(clientHandle, adapterGuid, bssidBytes);

                    return (ssid, signal, band, channel, auth, bssLimitation);
                }
                finally
                {
                    WlanFreeMemory(pData);
                }
            }
            finally
            {
                WlanFreeMemory(pIfList);
            }
        }
        catch (Exception ex)
        {
            return (null, null, null, null, null, $"Wi-Fi metadata query failed ({ex.GetType().Name}).");
        }
        finally
        {
            if (clientHandle != 0) WlanCloseHandle(clientHandle, 0);
        }
    }

    private (string? Band, int? Channel, string Limitation) ReadFrequencyAndChannel(
        nint clientHandle,
        Guid adapterGuid,
        ReadOnlySpan<byte> connectedBssid)
    {
        int? fallbackChannel = null;

        // Query channel opcode directly for authoritative channel number fallback
        var chRes = WlanQueryInterface(clientHandle, in adapterGuid, WlanIntfOpcodeChannelNumber, 0, out _, out var pChData, out _);
        if (chRes == 0 && pChData != 0)
        {
            try
            {
                var ch = (int)*(uint*)pChData;
                if (ch > 0) fallbackChannel = ch;
            }
            finally
            {
                WlanFreeMemory(pChData);
            }
        }

        // Query BSS list to obtain actual center frequency for the matching BSSID.
        // Pass dot11_BSS_type_any (3) to ensure infrastructure and mesh BSSs are visible,
        // and pDot11Ssid = 0 (null) so no visible BSS is excluded by SSID filtering.
        uint bssRes;
        nint pBssList;
        if (customBssFetcher != null)
        {
            bssRes = customBssFetcher(clientHandle, adapterGuid, out pBssList);
        }
        else
        {
            bssRes = WlanGetNetworkBssList(clientHandle, in adapterGuid, 0, 3 /* dot11_BSS_type_any */, 0, 0, out pBssList);
        }

        if (bssRes != 0 || pBssList == 0)
        {
            return ("Band unavailable", fallbackChannel, $"WLAN BSS list query failed (code {bssRes}).");
        }

        try
        {
            return CorrelateBssDetails(pBssList, connectedBssid, fallbackChannel);
        }
        finally
        {
            WlanFreeMemory(pBssList);
        }
    }

    public static bool AreBssidsEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length < 6 || b.Length < 6) return false;
        return a[0] == b[0] &&
               a[1] == b[1] &&
               a[2] == b[2] &&
               a[3] == b[3] &&
               a[4] == b[4] &&
               a[5] == b[5];
    }

    private static bool IsZeroMac(ReadOnlySpan<byte> mac)
    {
        for (int i = 0; i < 6; i++)
        {
            if (mac[i] != 0) return false;
        }
        return true;
    }

    public static (string? Band, int? Channel, string Limitation) CorrelateBssDetails(
        nint pBssList,
        ReadOnlySpan<byte> connectedBssid,
        int? fallbackChannel = null)
    {
        if (connectedBssid.Length < 6 || IsZeroMac(connectedBssid))
        {
            return ("Band unavailable", fallbackChannel, "Active connection BSSID is unavailable or unassociated.");
        }

        if (pBssList == 0)
        {
            return ("Band unavailable", fallbackChannel, "WLAN BSS list query unavailable.");
        }

        var header = *(WlanBssListHeader*)pBssList;
        if (header.NumberOfItems == 0)
        {
            return ("Band unavailable", fallbackChannel, "WLAN BSS list contains no visible access points.");
        }

        uint matchedFrequencyKhz = 0;
        bool matched = false;

        // Iterate through all BSS entries and find the exact matching BSSID.
        // Never assume list ordering or choose the first visible entry.
        for (uint i = 0; i < header.NumberOfItems; i++)
        {
            var pEntry = (WlanBssEntry*)((byte*)pBssList + 8 + (i * (uint)sizeof(WlanBssEntry)));
            var entryBssid = new ReadOnlySpan<byte>(pEntry->Dot11Bssid, 6);
            if (AreBssidsEqual(entryBssid, connectedBssid))
            {
                matchedFrequencyKhz = pEntry->ChCenterFrequency;
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            // Active connected BSSID was not present in the returned scan list.
            // Under PRD truth rules, do not borrow frequency from another BSS.
            return ("Band unavailable", fallbackChannel, "Active connection BSSID was not found in visible WLAN BSS list; band is unavailable.");
        }

        string band;
        int? channel = fallbackChannel;

        if (matchedFrequencyKhz >= 2400000 && matchedFrequencyKhz <= 2500000)
        {
            band = "2.4 GHz";
            channel ??= matchedFrequencyKhz == 2484000 ? 14 : (int)((matchedFrequencyKhz - 2407000) / 5000);
        }
        else if (matchedFrequencyKhz >= 4900000 && matchedFrequencyKhz < 5925000)
        {
            band = "5 GHz";
            channel ??= (int)((matchedFrequencyKhz - 5000000) / 5000);
        }
        else if (matchedFrequencyKhz >= 5925000 && matchedFrequencyKhz <= 7200000)
        {
            band = "6 GHz";
            channel ??= (int)((matchedFrequencyKhz - 5950000) / 5000);
        }
        else
        {
            band = "Band unavailable";
        }

        return (band, channel, "Wi-Fi connection metadata observed from matching Windows WLAN BSS.");
    }

    private static string MapAuthAlgorithm(int algo) => algo switch
    {
        1 => "Open",
        2 => "Shared",
        3 => "WPA-Enterprise",
        4 => "WPA-Personal",
        5 => "WPA-Enterprise",
        6 => "WPA2-Enterprise",
        7 => "WPA2-Personal",
        8 => "WPA3-Enterprise",
        9 => "WPA3-Personal",
        10 => "WPA3-Personal",
        11 => "OWE",
        _ => "Protected"
    };

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanOpenHandle(uint dwClientVersion, nint pReserved, out uint pdwNegotiatedVersion, out nint phClientHandle);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanCloseHandle(nint hClientHandle, nint pReserved);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanEnumInterfaces(nint hClientHandle, nint pReserved, out nint ppInterfaceList);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanQueryInterface(
        nint hClientHandle,
        in Guid pInterfaceGuid,
        int opCode,
        nint pReserved,
        out uint pdwDataSize,
        out nint ppData,
        out int pWlanOpcodeValueType);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern uint WlanGetNetworkBssList(
        nint hClientHandle,
        in Guid pInterfaceGuid,
        nint pDot11Ssid,
        int dot11BssType,
        int bSecurityEnabled,
        nint pReserved,
        out nint ppWlanBssList);

    [DllImport("wlanapi.dll", ExactSpelling = true)]
    private static extern void WlanFreeMemory(nint pMemory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        public fixed char Description[256];
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanInterfaceInfoListHeader
    {
        public uint NumberOfItems;
        public uint Index;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Dot11Ssid
    {
        public uint Length;
        public fixed byte Bytes[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanAssociationAttributes
    {
        public Dot11Ssid Dot11Ssid;
        public int Dot11BssType;
        public fixed byte Dot11Bssid[6];
        public int Dot11PhyType;
        public uint Dot11PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanSecurityAttributes
    {
        public int SecurityEnabled;
        public int OneXEnabled;
        public int Dot11AuthAlgorithm;
        public int Dot11CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WlanConnectionAttributes
    {
        public int IsState;
        public int WlanConnectionMode;
        public fixed char ProfileName[256];
        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanRateSet
    {
        public uint RateSetLength;
        public fixed ushort RateSet[126];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanBssEntry
    {
        public Dot11Ssid Dot11Ssid;
        public uint PhyId;
        public fixed byte Dot11Bssid[6];
        public int Dot11BssType;
        public int Dot11BssPhyType;
        public int Rssi;
        public uint LinkQuality;
        public byte InRegDomain;
        public ushort BeaconPeriod;
        public ulong Timestamp;
        public ulong HostTimestamp;
        public ushort CapabilityInformation;
        public uint ChCenterFrequency;
        public WlanRateSet RateSet;
        public uint IeOffset;
        public uint IeSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WlanBssListHeader
    {
        public uint TotalSize;
        public uint NumberOfItems;
    }
}

public sealed class WindowsNetworkProfileNameProvider : INetworkProfileNameProvider
{
    public string? GetProfileName(Guid adapterGuid)
    {
        if (!OperatingSystem.IsWindows() || adapterGuid == Guid.Empty) return null;
        try
        {
            var clsid = new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
            var type = Type.GetTypeFromCLSID(clsid);
            if (type is null) return null;
            var nlm = Activator.CreateInstance(type) as INetworkListManager;
            if (nlm is null) return null;

            if (nlm.GetNetworkConnections(out var enumConns) != 0 || enumConns is null)
                return null;

            foreach (var item in enumConns)
            {
                if (item is INetworkConnection conn)
                {
                    if (conn.GetAdapterId(out var connAdapterId) == 0 && connAdapterId == adapterGuid)
                    {
                        if (conn.GetNetwork(out var network) == 0 && network is not null)
                        {
                            if (network.GetName(out var name) == 0 && !string.IsNullOrWhiteSpace(name))
                            {
                                return name.Trim();
                            }
                        }
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    [ComImport, Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
    private class NetworkListManagerClass { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    private interface INetworkListManager
    {
        [PreserveSig]
        int GetNetworks(int Flags, [Out, MarshalAs(UnmanagedType.Interface)] out IEnumerable ppEnumNetwork);
        [PreserveSig]
        int GetNetwork(Guid gdNetworkId, [Out, MarshalAs(UnmanagedType.Interface)] out INetwork ppNetwork);
        [PreserveSig]
        int GetNetworkConnections([Out, MarshalAs(UnmanagedType.Interface)] out IEnumerable ppEnum);
        [PreserveSig]
        int GetNetworkConnection(Guid gdNetworkConnectionId, [Out, MarshalAs(UnmanagedType.Interface)] out INetworkConnection ppNetworkConnection);
        [PreserveSig]
        int IsConnectedToInternet([MarshalAs(UnmanagedType.Bool)] out bool pbIsConnected);
        [PreserveSig]
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool pbIsConnected);
        [PreserveSig]
        int GetConnectivity(out int pConnectivity);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
    private interface INetworkConnection
    {
        [PreserveSig]
        int GetNetwork([Out, MarshalAs(UnmanagedType.Interface)] out INetwork ppNetwork);
        [PreserveSig]
        int IsConnectedToInternet([MarshalAs(UnmanagedType.Bool)] out bool pbIsConnected);
        [PreserveSig]
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool pbIsConnected);
        [PreserveSig]
        int GetConnectivity(out int pConnectivity);
        [PreserveSig]
        int GetConnectionId(out Guid pgdConnectionId);
        [PreserveSig]
        int GetAdapterId(out Guid pgdAdapterId);
        [PreserveSig]
        int GetDomainType(out int pDomainType);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("DCB00001-570F-4A9B-8D69-199FDBA5723B")]
    private interface INetwork
    {
        [PreserveSig]
        int GetName([Out, MarshalAs(UnmanagedType.BStr)] out string pszNetworkName);
        [PreserveSig]
        int SetName([MarshalAs(UnmanagedType.BStr)] string szNetworkName);
        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.BStr)] out string pszDescription);
        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.BStr)] string szDescription);
        [PreserveSig]
        int GetNetworkId(out Guid pgdGuidNetworkId);
    }
}
