using System.ComponentModel;
using System.Runtime.InteropServices;
using SpeedMeter.Core;

namespace SpeedMeter.Windows;

/// <summary>Standard-user IP Helper counters, no active probes or shell processes.</summary>
public sealed unsafe class AdapterProvider : IAdapterCounterProvider
{
    public AdapterObservation ReadSnapshot()
    {
        if (!OperatingSystem.IsWindows())
            return new AdapterObservation(ProviderAvailability.Unsupported, [], "GetIfTable2", "platform.unsupported", "Windows is required.");
        // MIB_IF_TABLE2's first row is aligned to eight bytes on the supported x64 platform.
        if (!Environment.Is64BitProcess || sizeof(InterfaceRow) != 1352)
            return new AdapterObservation(ProviderAvailability.Unsupported, [], "GetIfTable2", "platform.architecture", "The validated provider requires Windows x64 and a 1352-byte MIB_IF_ROW2.");

        var error = GetIfTable2(out var table);
        if (error != 0)
        {
            var availability = error switch
            {
                5 => ProviderAvailability.PermissionDenied,
                _ => ProviderAvailability.Failed
            };
            return new AdapterObservation(availability, [], "GetIfTable2", $"win32.{error}", $"GetIfTable2 returned Win32 error {error}.");
        }
        try
        {
            var count = *(uint*)table;
            var rows = (InterfaceRow*)((byte*)table + 8);
            var result = new List<AdapterCounter>();
            for (var i = 0; i < count; i++)
            {
                var row = rows + i;
                if (row->Type == 24) continue; // Software loopback is not an eligible normal scope.
                result.Add(new AdapterCounter(row->Guid.ToString("D"), new string(row->Alias),
                    new string(row->Description), row->OperStatus == 1,
                    row->InOctets, row->OutOctets, row->ReceiveLinkSpeed, (row->Flags & 1) != 0));
            }
            var sorted = result.OrderByDescending(a => a.IsUp).ThenByDescending(a => a.IsHardware).ThenBy(a => a.Name).ToArray();
            return new AdapterObservation(ProviderAvailability.Available, sorted, "GetIfTable2", "ok", "Observed via GetIfTable2.");
        }
        catch (Exception ex)
        {
            return new AdapterObservation(ProviderAvailability.Failed, [], "GetIfTable2", "read.exception", "Error parsing interface table: " + ex.Message);
        }
        finally { FreeMibTable(table); }
    }

    public IReadOnlyList<AdapterCounter> Read()
    {
        var snapshot = ReadSnapshot();
        if (snapshot.Availability != ProviderAvailability.Available)
        {
            if (snapshot.Availability == ProviderAvailability.Unsupported)
                throw new PlatformNotSupportedException(snapshot.Detail);
            throw new Win32Exception(snapshot.Detail);
        }
        return snapshot.Adapters;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIfTable2(out nint table);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeMibTable(nint table);

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
