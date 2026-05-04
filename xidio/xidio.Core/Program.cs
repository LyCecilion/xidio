using System.Net;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace xidio.Core
{
    enum PrimaryInterfaceKind
    {
        Ethernet,
        Wireless,
        Pppoe
    }

    class PrimaryInterfaceInfo
    {
        public required NetworkInterface Interface { get; init; }
        public required PrimaryInterfaceKind Kind { get; init; }
    }

    class NetworkAdapterDriverInfo
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string DriverVersion { get; init; }
    }

    class WirelessConnectionInfo
    {
        public required string Ssid { get; init; }
        public required string Bssid { get; init; }
        public string ApMac => Bssid;
    }

    class InterfaceRouteInfo
    {
        public required string DestinationPrefix { get; init; }
        public required string NextHop { get; init; }
        public required int AddressFamily { get; init; }
        public required int RouteMetric { get; init; }
        public required int InterfaceMetric { get; init; }
        public int TotalMetric => RouteMetric + InterfaceMetric;
    }

    class InterfaceMetricInfo
    {
        public required int AddressFamily { get; init; }
        public required int InterfaceMetric { get; init; }
    }

    static class WirelessConnectionInfoProvider
    {
        public static WirelessConnectionInfo? GetConnectedNetwork(NetworkInterface ni)
        {
            if (!OperatingSystem.IsWindows())
                return null;

            if (!Guid.TryParse(ni.Id, out var interfaceGuid))
                return null;

            try
            {
                return WindowsWlanApi.GetCurrentConnection(interfaceGuid);
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }
    }

    static class WindowsWlanApi
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

    static class NetworkInterfaceDetector
    {
        public static List<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfos()
        {
            var driverVersions = GetDriverVersionsByPnpDeviceId();
            var result = new List<NetworkAdapterDriverInfo>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description, NetConnectionID, PhysicalAdapter, " +
                "PNPDeviceID, Manufacturer, ServiceName " +
                "FROM Win32_NetworkAdapter " +
                "WHERE PhysicalAdapter = TRUE AND PNPDeviceID IS NOT NULL");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var description = obj["Description"]?.ToString() ?? "";
                var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
                var serviceName = obj["ServiceName"]?.ToString() ?? "";
                var netConnectionId = obj["NetConnectionID"]?.ToString() ?? "";

                if (LooksLikeVirtualAdapter(
                        name,
                        description,
                        manufacturer,
                        pnpDeviceId,
                        serviceName,
                        netConnectionId))
                {
                    continue;
                }

                driverVersions.TryGetValue(pnpDeviceId, out var driverVersion);

                result.Add(new NetworkAdapterDriverInfo
                {
                    Name = string.IsNullOrWhiteSpace(netConnectionId) ? name : netConnectionId,
                    Description = description,
                    DriverVersion = string.IsNullOrWhiteSpace(driverVersion) ? "<unknown>" : driverVersion
                });
            }

            return result;
        }

        public static List<PrimaryInterfaceInfo> GetPrimaryInterfaces()
        {
            var physicalAdapterGuids = GetPhysicalAdapterGuids();
            var result = new List<PrimaryInterfaceInfo>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var type = ni.NetworkInterfaceType;
                // 1. PPP / PPPoE 单独处理
                if (type == NetworkInterfaceType.Ppp)
                {
                    if (LooksLikeRealPppInterface(ni))
                    {
                        result.Add(new PrimaryInterfaceInfo
                        {
                            Interface = ni,
                            Kind = PrimaryInterfaceKind.Pppoe
                        });
                    }

                    continue;
                }

                // 2. 有线 / 无线必须同时满足：
                //    - 类型是 Ethernet / Wireless80211
                //    - WMI 认为它是 PhysicalAdapter
                if (!physicalAdapterGuids.Contains(NormalizeGuid(ni.Id)))
                    continue;
                if (type == NetworkInterfaceType.Wireless80211)
                {
                    result.Add(new PrimaryInterfaceInfo
                    {
                        Interface = ni,
                        Kind = PrimaryInterfaceKind.Wireless
                    });
                }
                else if (IsEthernetType(type))
                {
                    // 排除蓝牙 PAN
                    if (LooksLikeBluetooth(ni))
                        continue;
                    result.Add(new PrimaryInterfaceInfo
                    {
                        Interface = ni,
                        Kind = PrimaryInterfaceKind.Ethernet
                    });
                }
            }

            return result;
        }

        public static List<InterfaceMetricInfo> GetInterfaceMetrics(NetworkInterface ni)
        {
            var result = new List<InterfaceMetricInfo>();

            if (!OperatingSystem.IsWindows())
                return result;

            try
            {
                foreach (var interfaceIndex in GetInterfaceIndices(ni))
                {
                    using var searcher = new ManagementObjectSearcher(
                        new ManagementScope(@"\\.\ROOT\StandardCimv2"),
                        new ObjectQuery(
                            "SELECT AddressFamily, InterfaceMetric " +
                            "FROM MSFT_NetIPInterface " +
                            $"WHERE InterfaceIndex = {interfaceIndex}"));

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var addressFamily = GetWmiInt32(obj, "AddressFamily");
                        var interfaceMetric = GetWmiInt32(obj, "InterfaceMetric");

                        if (addressFamily == 0)
                            continue;

                        result.Add(new InterfaceMetricInfo
                        {
                            AddressFamily = addressFamily,
                            InterfaceMetric = interfaceMetric
                        });
                    }
                }
            }
            catch (ManagementException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }

            return result;
        }

        public static List<InterfaceRouteInfo> GetRoutes(NetworkInterface ni)
        {
            var result = new List<InterfaceRouteInfo>();

            if (!OperatingSystem.IsWindows())
                return result;

            try
            {
                foreach (var interfaceIndex in GetInterfaceIndices(ni))
                {
                    using var searcher = new ManagementObjectSearcher(
                        new ManagementScope(@"\\.\ROOT\StandardCimv2"),
                        new ObjectQuery(
                            "SELECT DestinationPrefix, NextHop, AddressFamily, RouteMetric, InterfaceMetric " +
                            "FROM MSFT_NetRoute " +
                            $"WHERE InterfaceIndex = {interfaceIndex}"));

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var destinationPrefix = obj["DestinationPrefix"]?.ToString();

                        if (string.IsNullOrWhiteSpace(destinationPrefix))
                            continue;

                        result.Add(new InterfaceRouteInfo
                        {
                            DestinationPrefix = destinationPrefix,
                            NextHop = obj["NextHop"]?.ToString() ?? "",
                            AddressFamily = GetWmiInt32(obj, "AddressFamily"),
                            RouteMetric = GetWmiInt32(obj, "RouteMetric"),
                            InterfaceMetric = GetWmiInt32(obj, "InterfaceMetric")
                        });
                    }
                }
            }
            catch (ManagementException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }

            return result;
        }

        private static Dictionary<string, string> GetDriverVersionsByPnpDeviceId()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver " +
                "WHERE DeviceClass = 'NET'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var deviceId = obj["DeviceID"]?.ToString();
                var driverVersion = obj["DriverVersion"]?.ToString();

                if (string.IsNullOrWhiteSpace(deviceId) ||
                    string.IsNullOrWhiteSpace(driverVersion))
                {
                    continue;
                }

                result[deviceId] = driverVersion;
            }

            return result;
        }

        private static List<int> GetInterfaceIndices(NetworkInterface ni)
        {
            var result = new List<int>();
            IPInterfaceProperties properties;

            try
            {
                properties = ni.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                return result;
            }

            try
            {
                var ipv4Index = properties.GetIPv4Properties()?.Index;

                if (ipv4Index is > 0)
                    result.Add(ipv4Index.Value);
            }
            catch (NetworkInformationException)
            {
            }

            try
            {
                var ipv6Index = properties.GetIPv6Properties()?.Index;

                if (ipv6Index is > 0 && !result.Contains(ipv6Index.Value))
                    result.Add(ipv6Index.Value);
            }
            catch (NetworkInformationException)
            {
            }

            return result;
        }

        private static int GetWmiInt32(ManagementBaseObject obj, string propertyName)
        {
            return int.TryParse(obj[propertyName]?.ToString(), out var value)
                ? value
                : 0;
        }

        private static bool IsEthernetType(NetworkInterfaceType type)
        {
            return type is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx
                or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.Ethernet3Megabit;
        }

        private static HashSet<string> GetPhysicalAdapterGuids()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var searcher = new ManagementObjectSearcher(
                "SELECT GUID, Name, Description, NetConnectionID, PhysicalAdapter, " +
                "PNPDeviceID, Manufacturer, ServiceName " +
                "FROM Win32_NetworkAdapter " +
                "WHERE PhysicalAdapter = TRUE AND GUID IS NOT NULL");

            foreach (ManagementObject obj in searcher.Get())
            {
                var guid = obj["GUID"]?.ToString();

                if (string.IsNullOrWhiteSpace(guid))
                    continue;

                var name = obj["Name"]?.ToString() ?? "";
                var description = obj["Description"]?.ToString() ?? "";
                var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
                var serviceName = obj["ServiceName"]?.ToString() ?? "";
                var netConnectionId = obj["NetConnectionID"]?.ToString() ?? "";

                if (LooksLikeVirtualAdapter(
                        name,
                        description,
                        manufacturer,
                        pnpDeviceId,
                        serviceName,
                        netConnectionId))
                {
                    continue;
                }

                set.Add(NormalizeGuid(guid));
            }

            return set;
        }

        private static string NormalizeGuid(string guid)
        {
            return guid.Trim().Trim('{', '}').ToUpperInvariant();
        }

        private static bool LooksLikeBluetooth(NetworkInterface ni)
        {
            return LooksLikeBluetooth(ni.Name, ni.Description);
        }

        private static bool LooksLikeBluetooth(string name, string descriptionOrPnpId)
        {
            var text = $"{name} {descriptionOrPnpId}".ToLowerInvariant();
            return text.Contains("bluetooth")
                   || text.Contains("蓝牙")
                   || text.StartsWith("bth\\");
        }

        private static bool LooksLikeRealPppInterface(NetworkInterface ni)
        {
            var text = $"{ni.Name} {ni.Description}".ToLowerInvariant();
            // 排除 NDIS 过滤器派生项
            if (text.Contains("npcap"))
                return false;
            if (text.Contains("packet scheduler"))
                return false;
            if (text.Contains("qos"))
                return false;
            if (text.Contains("wfp"))
                return false;
            if (text.Contains("filter"))
                return false;
            return true;
        }

        private static bool LooksLikeVirtualAdapter(
            string name,
            string description,
            string manufacturer = "",
            string pnpDeviceId = "",
            string serviceName = "",
            string netConnectionId = "")
        {
            string text = string.Join(" ", new[]
            {
                name,
                description,
                manufacturer,
                pnpDeviceId,
                serviceName,
                netConnectionId
            }).ToLowerInvariant();

            return
                // VMware
                text.Contains("vmware") ||
                text.Contains("vmnet") ||

                // Hyper-V
                text.Contains("hyper-v") ||
                text.Contains("hyperv") ||
                text.Contains("vswitch") ||
                text.Contains("vethernet") ||

                // VirtualBox
                text.Contains("virtualbox") ||
                text.Contains("host-only") ||

                // WSL / NAT / virtual switch
                text.Contains("wsl") ||

                // VPN / virtual NICs, depending on whether you want to exclude them
                text.Contains("tap-windows") ||
                text.Contains("tap adapter") ||
                text.Contains("tun adapter") ||
                text.Contains("wireguard") ||
                text.Contains("tailscale") ||
                text.Contains("zerotier") ||

                // Packet capture / filter drivers
                text.Contains("npcap") ||
                text.Contains("winpcap") ||
                text.Contains("packet driver") ||

                // Microsoft virtual Wi-Fi / Wi-Fi Direct
                text.Contains("wi-fi direct") ||
                text.Contains("wifi direct") ||
                text.Contains("virtual wifi") ||

                // Bluetooth PAN
                text.Contains("bluetooth") ||
                text.Contains("蓝牙");
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            PrintBanner();

            Console.WriteLine("Now we're going to collect your device's basic network environment.\n");
            var timestamp = PrintTimestamp();
            PromptLocation();
            var primaryNI = PrintNetworkInterfaces();
            var hostname = PrintHostname();
            var isProxyOn = PrintProxyStatus();
        }

        private static void PrintBanner()
        {
            Console.WriteLine("""
                              ====================================================

                                ___   ___  __   _______   __    ______
                                \  \ /  / |  | |       \ |  |  /  __  \
                                 \  V  /  |  | |  .--.  ||  | |  |  |  |
                                  >   <   |  | |  |  |  ||  | |  |  |  |
                                 /  .  \  |  | |  '--'  ||  | |  `--'  |
                                /__/ \__\ |__| |_______/ |__|  \______/   v0.1.0

                               Xidian Internet Diagnostic Intelligence Operator
                                   Network intelligence at your fingertips.

                               Early Development build — expect changes and bugs.
                                   Built by LyCecilion and xilin, with love.
                                      Be affiliated with Project Hazelita.

                              ====================================================

                              """);
        }

        private static long PrintTimestamp()
        {
            var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Console.WriteLine($"Current time: {currentTime}. In Unix timestamp: {unixTimestamp}.\n");
            return unixTimestamp;
        }

        private static void PromptLocation()
        {
            // User's Location
            Console.WriteLine("""
                              We need to know your current location to better assist you.
                              Hmm... where are you right now? Or, if you're not with your device, where is your device?

                                  (1) Dormitory Building (Haitang, Zhuyuan or Dingxiang)
                                  (2) Library
                                  (3) Teaching Building (A, B, C, D, E or Xinyuan)
                                  (4) Cybersecurity Building (Sec, AI, CS or Mechatronics)
                                  (5) Faculty Residential Area
                                  (6) Complex Building (New or Old)
                                  (7) Roads
                                  (8) Others

                              """);
            // [TODO] collect user's choice
        }

        private static List<PrimaryInterfaceInfo> PrintNetworkInterfaces()
        {
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var adapterDriverInfos = NetworkInterfaceDetector.GetNetworkAdapterDriverInfos();
            var primaryInterfaces = NetworkInterfaceDetector.GetPrimaryInterfaces();

            Console.WriteLine(
                $"We detected {allInterfaces.Length} network interface(s), among which {primaryInterfaces.Count} seem(s) to be primary interfaces.\n");

            Console.WriteLine("Below are detected physical network adapters and driver versions.\n");

            foreach (var adapter in adapterDriverInfos)
            {
                Console.WriteLine(
                    $"    {adapter.Name} | {adapter.Description} | Driver Version: {adapter.DriverVersion}");
            }

            Console.WriteLine();
            Console.WriteLine("Below are detected primary connections.\n");

            foreach (var item in primaryInterfaces)
            {
                var ni = item.Interface;

                Console.WriteLine(
                    $"    {item.Kind} | {ni.Name} | {ni.Description} | {ni.OperationalStatus}");

                if (item.Kind == PrimaryInterfaceKind.Wireless &&
                    ni.OperationalStatus == OperationalStatus.Up)
                {
                    var wirelessInfo = WirelessConnectionInfoProvider.GetConnectedNetwork(ni);

                    if (wirelessInfo is not null)
                    {
                        Console.WriteLine(
                            $"        SSID: {wirelessInfo.Ssid} | BSSID: {wirelessInfo.Bssid} | AP MAC: {wirelessInfo.ApMac}");
                    }
                    else
                    {
                        Console.WriteLine("        Wireless connection details are unavailable.");
                    }
                }

                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    PrintPrimaryInterfaceNetworkDetails(ni);
                }
            }

            return primaryInterfaces;
        }

        private static void PrintPrimaryInterfaceNetworkDetails(NetworkInterface ni)
        {
            IPInterfaceProperties properties;

            try
            {
                properties = ni.GetIPProperties();
            }
            catch (NetworkInformationException ex)
            {
                Console.WriteLine($"        IP details are unavailable: {ex.Message}");
                return;
            }

            var routes = NetworkInterfaceDetector.GetRoutes(ni);
            var metrics = NetworkInterfaceDetector.GetInterfaceMetrics(ni);

            Console.WriteLine($"        IPv4 Address(es): {FormatIpv4UnicastAddresses(properties.UnicastAddresses)}");
            Console.WriteLine($"        IPv6 Address(es): {FormatIpv6UnicastAddresses(properties.UnicastAddresses)}");
            Console.WriteLine($"        Default Gateway(s): {FormatGatewayAddresses(properties.GatewayAddresses)}");
            Console.WriteLine($"        DHCP Server(s): {FormatIpAddresses(properties.DhcpServerAddresses)}");
            Console.WriteLine($"        DNS Server(s): {FormatIpAddresses(properties.DnsAddresses)}");
            Console.WriteLine($"        Interface Metric: {FormatInterfaceMetrics(metrics, routes)}");
            Console.WriteLine("        Route Table Summary:");
            PrintRouteFamilySummary("IPv4", routes, (int)AddressFamily.InterNetwork);
            PrintRouteFamilySummary("IPv6", routes, (int)AddressFamily.InterNetworkV6);
        }

        private static string FormatIpv4UnicastAddresses(UnicastIPAddressInformationCollection addresses)
        {
            var formatted = addresses
                .Where(addressInfo => addressInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(addressInfo =>
                {
                    var mask = addressInfo.IPv4Mask?.ToString();
                    var prefix = addressInfo.PrefixLength >= 0 ? $"/{addressInfo.PrefixLength}" : "";

                    return string.IsNullOrWhiteSpace(mask)
                        ? $"{addressInfo.Address}{prefix} (subnet mask unavailable)"
                        : $"{addressInfo.Address}{prefix} (subnet mask {mask})";
                })
                .ToList();

            return formatted.Count == 0 ? "<none>" : string.Join(", ", formatted);
        }

        private static string FormatIpv6UnicastAddresses(UnicastIPAddressInformationCollection addresses)
        {
            var formatted = addresses
                .Where(addressInfo => addressInfo.Address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(addressInfo => $"{addressInfo.Address}/{addressInfo.PrefixLength}")
                .ToList();

            return formatted.Count == 0 ? "<none>" : string.Join(", ", formatted);
        }

        private static string FormatGatewayAddresses(GatewayIPAddressInformationCollection addresses)
        {
            return FormatIpAddresses(addresses.Select(addressInfo => addressInfo.Address));
        }

        private static string FormatIpAddresses(IEnumerable<IPAddress> addresses)
        {
            var formatted = addresses
                .Select(address => address.ToString())
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToList();

            return formatted.Count == 0 ? "<none>" : string.Join(", ", formatted);
        }

        private static string FormatInterfaceMetrics(
            List<InterfaceMetricInfo> metrics,
            List<InterfaceRouteInfo> routes)
        {
            var formattedMetrics = metrics
                .GroupBy(metric => metric.AddressFamily)
                .Select(group => group.First())
                .OrderBy(metric => metric.AddressFamily)
                .Select(metric => $"{FormatAddressFamily(metric.AddressFamily)}: {metric.InterfaceMetric}")
                .ToList();

            if (formattedMetrics.Count == 0)
            {
                formattedMetrics = routes
                    .GroupBy(route => route.AddressFamily)
                    .Select(group => group.First())
                    .OrderBy(route => route.AddressFamily)
                    .Select(route => $"{FormatAddressFamily(route.AddressFamily)}: {route.InterfaceMetric}")
                    .ToList();
            }

            return formattedMetrics.Count == 0 ? "<unavailable>" : string.Join(", ", formattedMetrics);
        }

        private static void PrintRouteFamilySummary(
            string label,
            List<InterfaceRouteInfo> routes,
            int addressFamily)
        {
            var familyRoutes = routes
                .Where(route => route.AddressFamily == addressFamily)
                .OrderBy(route => route.TotalMetric)
                .ThenBy(route => route.RouteMetric)
                .ThenBy(route => route.DestinationPrefix)
                .ToList();

            if (familyRoutes.Count == 0)
            {
                Console.WriteLine($"            {label}: <none>");
                return;
            }

            var defaultRoutes = familyRoutes
                .Where(IsDefaultRoute)
                .Take(3)
                .Select(FormatRoute)
                .ToList();
            var sampleRoutes = familyRoutes
                .Where(route => !IsDefaultRoute(route))
                .Take(3)
                .Select(FormatRoute)
                .ToList();
            var defaultRouteText = defaultRoutes.Count == 0
                ? "<none>"
                : string.Join("; ", defaultRoutes);
            var sampleRouteText = sampleRoutes.Count == 0
                ? "<none>"
                : string.Join("; ", sampleRoutes);

            Console.WriteLine(
                $"            {label}: {familyRoutes.Count} route(s); default: {defaultRouteText}; sample: {sampleRouteText}");
        }

        private static bool IsDefaultRoute(InterfaceRouteInfo route)
        {
            return route.DestinationPrefix is "0.0.0.0/0" or "::/0";
        }

        private static string FormatRoute(InterfaceRouteInfo route)
        {
            var nextHop = string.IsNullOrWhiteSpace(route.NextHop) ||
                          route.NextHop is "0.0.0.0" or "::"
                ? "on-link"
                : $"via {route.NextHop}";

            return $"{route.DestinationPrefix} {nextHop} (route {route.RouteMetric}, interface {route.InterfaceMetric}, total {route.TotalMetric})";
        }

        private static string FormatAddressFamily(int addressFamily)
        {
            return addressFamily == (int)AddressFamily.InterNetwork
                ? "IPv4"
                : addressFamily == (int)AddressFamily.InterNetworkV6
                    ? "IPv6"
                    : $"AF{addressFamily}";
        }

        private static string PrintHostname()
        {
            var hostName = Dns.GetHostName();
            Console.WriteLine($"Hostname: {hostName}");
            return hostName;
        }

        private static bool PrintProxyStatus()
        {
            var handler = new HttpClientHandler();
            var defaultProxy = handler.Proxy ?? WebRequest.GetSystemWebProxy();
            var proxyUri = defaultProxy?.GetProxy(new Uri("https://xidio.stellalyr.ink"));
            bool isProxyOn;
            if (proxyUri != null && proxyUri != new Uri("https://xidio.stellalyr.ink"))
            {
                Console.WriteLine($"System Proxy is on. The proxy URL is {proxyUri}.");
                isProxyOn = true;
            }
            else
            {
                Console.WriteLine("System Proxy is off.");
                isProxyOn = false;
            }
            return isProxyOn;
        }
    }
}
