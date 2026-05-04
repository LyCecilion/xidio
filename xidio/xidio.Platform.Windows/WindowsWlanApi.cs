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

                return new WirelessConnectionInfo
                {
                    Ssid = DecodeSsid(attributes.AssociationAttributes.Dot11Ssid),
                    Bssid = FormatMacAddress(attributes.AssociationAttributes.Dot11Bssid)
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
}
