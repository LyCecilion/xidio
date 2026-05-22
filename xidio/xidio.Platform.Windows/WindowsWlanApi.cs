using System.Runtime.InteropServices;
using System.Text;
using xidio.Core.Models;

namespace xidio.Platform.Windows;

internal static class WindowsWlanApi
{
    private const uint ErrorSuccess = 0;
    private const uint WlanClientVersion = 2;
    private const int WlanMaxNameLength = 256;
    private const int Dot11SsidMaxLength = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WirelessConnectionInfo? GetCurrentConnection(Guid interfaceGuid)
    {
        var openResult = WlanOpenHandle(
            WlanClientVersion,
            IntPtr.Zero,
            out _,
            out var clientHandle);

        if (openResult != ErrorSuccess)
            return null;

        try
        {
            var queryResult = WlanQueryInterface(
                clientHandle,
                ref interfaceGuid,
                WlanIntfOpcode.CurrentConnection,
                IntPtr.Zero,
                out _,
                out var data,
                out _);

            if (queryResult != ErrorSuccess || data == IntPtr.Zero)
                return null;

            try
            {
                var attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(data);

                if (attributes.IsState != WlanInterfaceState.Connected)
                    return null;

                var ssid = DecodeSsid(attributes.AssociationAttributes.Dot11Ssid);
                var bssid = FormatMacAddress(attributes.AssociationAttributes.Dot11Bssid);
                var bssDetails = SafeGet(() => GetCurrentBssDetails(clientHandle, interfaceGuid, bssid));
                var visibleNetworks = SafeGet(
                    () => GetAvailableNetworks(clientHandle, interfaceGuid)) ?? [];

                return new WirelessConnectionInfo
                {
                    Ssid = ssid,
                    Bssid = bssid,
                    SignalQualityPercent = (int)attributes.AssociationAttributes.WlanSignalQuality,
                    RssiDbm = bssDetails?.RssiDbm ?? EstimateRssi(attributes.AssociationAttributes.WlanSignalQuality),
                    Channel = bssDetails?.Channel,
                    FrequencyGhz = bssDetails?.FrequencyGhz,
                    PhyType = FormatPhyType(attributes.AssociationAttributes.Dot11PhyType),
                    Authentication = FormatAuthAlgorithm(attributes.SecurityAttributes.Dot11AuthAlgorithm),
                    Cipher = FormatCipherAlgorithm(attributes.SecurityAttributes.Dot11CipherAlgorithm),
                    ReceiveRateMbps = attributes.AssociationAttributes.RxRate / 1000d,
                    TransmitRateMbps = attributes.AssociationAttributes.TxRate / 1000d,
                    VisibleNetworks = visibleNetworks
                };
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }
        finally
        {
            WlanCloseHandle(clientHandle, IntPtr.Zero);
        }
    }

    private static T? SafeGet<T>(Func<T> getValue)
    {
        try
        {
            return getValue();
        }
        catch
        {
            return default;
        }
    }

    private static IReadOnlyList<VisibleWirelessNetworkInfo> GetAvailableNetworks(IntPtr clientHandle, Guid interfaceGuid)
    {
        var queryResult = WlanGetAvailableNetworkList(
            clientHandle,
            ref interfaceGuid,
            0,
            IntPtr.Zero,
            out var listPointer);

        if (queryResult != ErrorSuccess || listPointer == IntPtr.Zero)
            return Array.Empty<VisibleWirelessNetworkInfo>();

        try
        {
            var numberOfItems = Marshal.ReadInt32(listPointer);
            var itemPointer = listPointer + 8;
            var itemSize = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<VisibleWirelessNetworkInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < numberOfItems; index++)
            {
                var network = Marshal.PtrToStructure<WlanAvailableNetwork>(itemPointer + index * itemSize);
                var ssid = DecodeSsid(network.Dot11Ssid);

                if (string.IsNullOrWhiteSpace(ssid) || !seen.Add(ssid))
                    continue;

                networks.Add(new VisibleWirelessNetworkInfo
                {
                    Ssid = ssid,
                    SignalQualityPercent = (int)network.WlanSignalQuality,
                    Authentication = FormatAuthAlgorithm(network.Dot11DefaultAuthAlgorithm),
                    Cipher = FormatCipherAlgorithm(network.Dot11DefaultCipherAlgorithm),
                    BssidCount = (int)network.NumberOfBssids,
                    IsConnectable = network.NetworkConnectable != 0
                });
            }

            return networks;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static BssDetails? GetCurrentBssDetails(IntPtr clientHandle, Guid interfaceGuid, string currentBssid)
    {
        var queryResult = WlanGetNetworkBssList(
            clientHandle,
            ref interfaceGuid,
            IntPtr.Zero,
            Dot11BssType.Any,
            false,
            IntPtr.Zero,
            out var listPointer);

        if (queryResult != ErrorSuccess || listPointer == IntPtr.Zero)
            return null;

        try
        {
            var numberOfItems = Marshal.ReadInt32(listPointer);
            var itemPointer = listPointer + 8;
            var itemSize = Marshal.SizeOf<WlanBssEntry>();

            for (var index = 0; index < numberOfItems; index++)
            {
                var entry = Marshal.PtrToStructure<WlanBssEntry>(itemPointer + index * itemSize);
                var bssid = FormatMacAddress(entry.Dot11Bssid);

                if (!string.Equals(bssid, currentBssid, StringComparison.OrdinalIgnoreCase))
                    continue;

                return new BssDetails(
                    entry.Rssi,
                    FrequencyKHzToGhz(entry.ChCenterFrequency),
                    FrequencyKHzToChannel(entry.ChCenterFrequency));
            }

            return null;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static string DecodeSsid(Dot11Ssid dot11Ssid)
    {
        var bytes = dot11Ssid.Ssid ?? [];
        var length = Math.Min((int)dot11Ssid.SsidLength, bytes.Length);

        if (length <= 0)
            return "<hidden>";

        try
        {
            return StrictUtf8.GetString(bytes, 0, length);
        }
        catch (DecoderFallbackException)
        {
            var rawSsid = bytes.Take(length).ToArray();
            return $"0x{Convert.ToHexString(rawSsid)}";
        }
    }

    private static string FormatMacAddress(byte[] macAddress)
    {
        if (macAddress.Length == 0)
            return "<unknown>";

        return string.Join(":", macAddress.Select(static b => b.ToString("X2")));
    }

    private static int EstimateRssi(uint signalQuality)
    {
        return (int)(signalQuality / 2) - 100;
    }

    private static double? FrequencyKHzToGhz(uint frequencyKHz)
    {
        return frequencyKHz == 0 ? null : Math.Round(frequencyKHz / 1_000_000d, 3);
    }

    private static int? FrequencyKHzToChannel(uint frequencyKHz)
    {
        if (frequencyKHz == 0)
            return null;

        var frequencyMHz = frequencyKHz / 1000;

        if (frequencyMHz == 2484)
            return 14;
        if (frequencyMHz is >= 2412 and <= 2472)
            return (int)((frequencyMHz - 2407) / 5);
        if (frequencyMHz is >= 5000 and <= 5900)
            return (int)((frequencyMHz - 5000) / 5);
        if (frequencyMHz is >= 5955 and <= 7115)
            return (int)((frequencyMHz - 5950) / 5);

        return null;
    }

    private static string FormatPhyType(Dot11PhyType phyType)
    {
        return phyType switch
        {
            Dot11PhyType.Fhss => "FHSS",
            Dot11PhyType.Dsss => "DSSS",
            Dot11PhyType.Ofdm => "802.11a/g",
            Dot11PhyType.Hrdsss => "HR-DSSS",
            Dot11PhyType.Erp => "802.11g",
            Dot11PhyType.Ht => "802.11n",
            Dot11PhyType.Vht => "802.11ac",
            Dot11PhyType.He => "802.11ax",
            Dot11PhyType.Eht => "802.11be",
            _ => phyType.ToString()
        };
    }

    private static string FormatAuthAlgorithm(Dot11AuthAlgorithm authAlgorithm)
    {
        return authAlgorithm switch
        {
            Dot11AuthAlgorithm.Open => "Open",
            Dot11AuthAlgorithm.SharedKey => "Shared",
            Dot11AuthAlgorithm.Wpa => "WPA",
            Dot11AuthAlgorithm.WpaPsk => "WPA-PSK",
            Dot11AuthAlgorithm.Rsna => "WPA2",
            Dot11AuthAlgorithm.RsnaPsk => "WPA2-PSK",
            Dot11AuthAlgorithm.Wpa3 => "WPA3",
            Dot11AuthAlgorithm.Wpa3Sae => "WPA3-SAE",
            _ => authAlgorithm.ToString()
        };
    }

    private static string FormatCipherAlgorithm(Dot11CipherAlgorithm cipherAlgorithm)
    {
        return cipherAlgorithm switch
        {
            Dot11CipherAlgorithm.None => "None",
            Dot11CipherAlgorithm.Wep40 => "WEP-40",
            Dot11CipherAlgorithm.Tkip => "TKIP",
            Dot11CipherAlgorithm.Ccmp => "CCMP",
            Dot11CipherAlgorithm.Wep104 => "WEP-104",
            Dot11CipherAlgorithm.Wep => "WEP",
            Dot11CipherAlgorithm.Gcmp => "GCMP",
            Dot11CipherAlgorithm.Gcmp256 => "GCMP-256",
            Dot11CipherAlgorithm.Ccmp256 => "CCMP-256",
            _ => cipherAlgorithm.ToString()
        };
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        out uint pdwNegotiatedVersion,
        out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(
        IntPtr hClientHandle,
        IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        WlanIntfOpcode opCode,
        IntPtr pReserved,
        out uint pdwDataSize,
        out IntPtr ppData,
        out WlanOpcodeValueType pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        uint dwFlags,
        IntPtr pReserved,
        out IntPtr ppAvailableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetNetworkBssList(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid,
        Dot11BssType dot11BssType,
        bool bSecurityEnabled,
        IntPtr pReserved,
        out IntPtr ppWlanBssList);

    private enum WlanIntfOpcode
    {
        CurrentConnection = 7
    }

    private enum WlanOpcodeValueType
    {
        QueryOnly = 0,
        SetByGroupPolicy = 1,
        SetByUser = 2,
        Invalid = 3
    }

    private enum WlanInterfaceState
    {
        NotReady = 0,
        Connected = 1,
        AdHocNetworkFormed = 2,
        Disconnecting = 3,
        Disconnected = 4,
        Associating = 5,
        Discovering = 6,
        Authenticating = 7
    }

    private enum WlanConnectionMode
    {
        Profile = 0,
        TemporaryProfile = 1,
        DiscoverySecure = 2,
        DiscoveryUnsecure = 3,
        Auto = 4,
        Invalid = 5
    }

    private enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    private enum Dot11PhyType
    {
        Unknown = 0,
        Any = 0,
        Fhss = 1,
        Dsss = 2,
        IrBaseband = 3,
        Ofdm = 4,
        Hrdsss = 5,
        Erp = 6,
        Ht = 7,
        Vht = 8,
        Dmg = 9,
        He = 10,
        Eht = 11,
        IhvStart = unchecked((int)0x80000000),
        IhvEnd = unchecked((int)0xffffffff)
    }

    private enum Dot11AuthAlgorithm
    {
        Open = 1,
        SharedKey = 2,
        Wpa = 3,
        WpaPsk = 4,
        WpaNone = 5,
        Rsna = 6,
        RsnaPsk = 7,
        Wpa3 = 8,
        Wpa3Sae = 9,
        IhvStart = unchecked((int)0x80000000),
        IhvEnd = unchecked((int)0xffffffff)
    }

    private enum Dot11CipherAlgorithm
    {
        None = 0x00,
        Wep40 = 0x01,
        Tkip = 0x02,
        Ccmp = 0x04,
        Wep104 = 0x05,
        WpaUseGroup = 0x100,
        RsnUseGroup = 0x100,
        Wep = 0x101,
        Gcmp = 0x08,
        Gcmp256 = 0x09,
        Ccmp256 = 0x0a,
        IhvStart = unchecked((int)0x80000000),
        IhvEnd = unchecked((int)0xffffffff)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint SsidLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = Dot11SsidMaxLength)]
        public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Dot11Ssid;
        public Dot11BssType Dot11BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Dot11Bssid;

        public Dot11PhyType Dot11PhyType;
        public uint Dot11PhyIndex;
        public uint WlanSignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        public int SecurityEnabled;
        public int OneXEnabled;
        public Dot11AuthAlgorithm Dot11AuthAlgorithm;
        public Dot11CipherAlgorithm Dot11CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState IsState;
        public WlanConnectionMode WlanConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WlanMaxNameLength)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WlanMaxNameLength)]
        public string ProfileName;

        public Dot11Ssid Dot11Ssid;
        public Dot11BssType Dot11BssType;
        public uint NumberOfBssids;
        public int NetworkConnectable;
        public uint WlanNotConnectableReason;
        public uint NumberOfPhyTypes;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public Dot11PhyType[] Dot11PhyTypes;

        public int MorePhyTypes;
        public uint WlanSignalQuality;
        public int SecurityEnabled;
        public Dot11AuthAlgorithm Dot11DefaultAuthAlgorithm;
        public Dot11CipherAlgorithm Dot11DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanRateSet
    {
        public uint RateSetLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        public ushort[] RateSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanBssEntry
    {
        public Dot11Ssid Dot11Ssid;
        public uint PhyId;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Dot11Bssid;

        public Dot11BssType Dot11BssType;
        public Dot11PhyType Dot11BssPhyType;
        public int Rssi;
        public uint LinkQuality;
        public byte InRegDomain;
        public ushort BeaconPeriod;
        public ulong Timestamp;
        public ulong HostTimestamp;
        public ushort CapabilityInformation;
        public uint ChCenterFrequency;
        public WlanRateSet WlanRateSet;
        public uint IeOffset;
        public uint IeSize;
    }

    private sealed record BssDetails(int RssiDbm, double? FrequencyGhz, int? Channel);
}
