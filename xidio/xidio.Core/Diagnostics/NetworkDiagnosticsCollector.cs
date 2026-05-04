using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Core.Diagnostics;

public sealed class NetworkDiagnosticsCollector
{
    private readonly IPlatformNetworkDiagnosticsProvider _platformDiagnostics;

    public NetworkDiagnosticsCollector(IPlatformNetworkDiagnosticsProvider? platformDiagnostics = null)
    {
        _platformDiagnostics = platformDiagnostics ?? NoopPlatformNetworkDiagnosticsProvider.Instance;
    }

    public NetworkDiagnosticReport Collect()
    {
        var collectedAt = DateTimeOffset.Now;
        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var physicalInterfaceIds = SafeGet(_platformDiagnostics.GetPhysicalNetworkInterfaceIds, Array.Empty<string>());
        var adapterDriverInfos = SafeGet(_platformDiagnostics.GetNetworkAdapterDriverInfos, Array.Empty<NetworkAdapterDriverInfo>());
        var primaryInterfaces = GetPrimaryInterfaces(allInterfaces, physicalInterfaceIds);

        return new NetworkDiagnosticReport
        {
            CollectedAt = collectedAt,
            TotalNetworkInterfaceCount = allInterfaces.Length,
            HostName = Dns.GetHostName(),
            SystemProxy = GetSystemProxyInfo(),
            NetworkAdapterDriverInfos = adapterDriverInfos,
            PrimaryInterfaces = primaryInterfaces
        };
    }

    private IReadOnlyList<PrimaryInterfaceInfo> GetPrimaryInterfaces(
        IEnumerable<NetworkInterface> networkInterfaces,
        IReadOnlyCollection<string> physicalInterfaceIds)
    {
        var result = new List<PrimaryInterfaceInfo>();
        var hasPlatformPhysicalIds = physicalInterfaceIds.Count > 0;

        foreach (var networkInterface in networkInterfaces)
        {
            var kind = ClassifyPrimaryInterface(networkInterface, physicalInterfaceIds, hasPlatformPhysicalIds);

            if (kind is null)
                continue;

            var isUp = networkInterface.OperationalStatus == OperationalStatus.Up;
            var wirelessInfo = isUp && kind == PrimaryInterfaceKind.Wireless
                ? SafeGet(() => _platformDiagnostics.GetWirelessConnectionInfo(networkInterface), null)
                : null;
            var networkDetails = isUp
                ? GetNetworkDetails(networkInterface)
                : null;

            result.Add(new PrimaryInterfaceInfo
            {
                Id = networkInterface.Id,
                Name = networkInterface.Name,
                Description = networkInterface.Description,
                Kind = kind.Value,
                OperationalStatus = networkInterface.OperationalStatus,
                NetworkInterfaceType = networkInterface.NetworkInterfaceType,
                WirelessConnection = wirelessInfo,
                NetworkDetails = networkDetails
            });
        }

        return result;
    }

    private PrimaryInterfaceKind? ClassifyPrimaryInterface(
        NetworkInterface networkInterface,
        IReadOnlyCollection<string> physicalInterfaceIds,
        bool hasPlatformPhysicalIds)
    {
        var type = networkInterface.NetworkInterfaceType;

        if (type == NetworkInterfaceType.Ppp)
            return LooksLikeRealPppInterface(networkInterface) ? PrimaryInterfaceKind.Pppoe : null;

        if (!IsEthernetType(type) && type != NetworkInterfaceType.Wireless80211)
            return null;

        if (LooksLikeVirtualAdapter(networkInterface.Name, networkInterface.Description))
            return null;

        if (hasPlatformPhysicalIds && !physicalInterfaceIds.Contains(NormalizeGuid(networkInterface.Id)))
            return null;

        if (type == NetworkInterfaceType.Wireless80211)
            return PrimaryInterfaceKind.Wireless;

        return LooksLikeBluetooth(networkInterface.Name, networkInterface.Description)
            ? null
            : PrimaryInterfaceKind.Ethernet;
    }

    private InterfaceNetworkDetails? GetNetworkDetails(NetworkInterface networkInterface)
    {
        IPInterfaceProperties properties;

        try
        {
            properties = networkInterface.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        var routes = SafeGet(() => _platformDiagnostics.GetRoutes(networkInterface), Array.Empty<InterfaceRouteInfo>());
        var metrics = SafeGet(() => _platformDiagnostics.GetInterfaceMetrics(networkInterface), Array.Empty<InterfaceMetricInfo>());

        return new InterfaceNetworkDetails
        {
            IPv4Addresses = GetUnicastAddresses(properties, AddressFamily.InterNetwork),
            IPv6Addresses = GetUnicastAddresses(properties, AddressFamily.InterNetworkV6),
            DefaultGateways = properties.GatewayAddresses.Select(gateway => gateway.Address).ToList(),
            DhcpServers = GetDhcpServerAddresses(properties),
            DnsServers = properties.DnsAddresses.ToList(),
            InterfaceMetrics = metrics,
            Routes = routes
        };
    }

    private static IReadOnlyList<IPAddress> GetDhcpServerAddresses(IPInterfaceProperties properties)
    {
        return OperatingSystem.IsMacOS()
            ? Array.Empty<IPAddress>()
            : properties.DhcpServerAddresses.ToList();
    }

    private static IReadOnlyList<InterfaceIpAddressInfo> GetUnicastAddresses(
        IPInterfaceProperties properties,
        AddressFamily addressFamily)
    {
        return properties.UnicastAddresses
            .Where(address => address.Address.AddressFamily == addressFamily)
            .Select(address => new InterfaceIpAddressInfo
            {
                Address = address.Address,
                PrefixLength = address.PrefixLength,
                IPv4Mask = addressFamily == AddressFamily.InterNetwork ? address.IPv4Mask : null
            })
            .ToList();
    }

    private static SystemProxyInfo GetSystemProxyInfo()
    {
        var targetUri = new Uri("https://xidio.stellalyr.ink");
        var handler = new HttpClientHandler();
        var defaultProxy = handler.Proxy ?? WebRequest.GetSystemWebProxy();
        var proxyUri = defaultProxy?.GetProxy(targetUri);
        var isEnabled = proxyUri is not null && proxyUri != targetUri;

        return new SystemProxyInfo
        {
            IsEnabled = isEnabled,
            ProxyUri = isEnabled ? proxyUri : null
        };
    }

    private static bool IsEthernetType(NetworkInterfaceType type)
    {
        return type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.Ethernet3Megabit;
    }

    private static string NormalizeGuid(string guid)
    {
        return guid.Trim().Trim('{', '}').ToUpperInvariant();
    }

    private static bool LooksLikeBluetooth(string name, string descriptionOrPnpId)
    {
        var text = $"{name} {descriptionOrPnpId}".ToLowerInvariant();
        return text.Contains("bluetooth")
            || text.Contains("蓝牙")
            || text.StartsWith("bth\\");
    }

    private static bool LooksLikeRealPppInterface(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();

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

    private static bool LooksLikeVirtualAdapter(string name, string description)
    {
        var text = $"{name} {description}".ToLowerInvariant();

        return
            text.Contains("vmware") ||
            text.Contains("vmnet") ||
            text.Contains("hyper-v") ||
            text.Contains("hyperv") ||
            text.Contains("vswitch") ||
            text.Contains("vethernet") ||
            text.Contains("virtualbox") ||
            text.Contains("host-only") ||
            text.Contains("wsl") ||
            text.Contains("tap-windows") ||
            text.Contains("tap adapter") ||
            text.Contains("tun adapter") ||
            text.Contains("wireguard") ||
            text.Contains("tailscale") ||
            text.Contains("zerotier") ||
            text.Contains("npcap") ||
            text.Contains("winpcap") ||
            text.Contains("packet driver") ||
            text.Contains("wi-fi direct") ||
            text.Contains("wifi direct") ||
            text.Contains("virtual wifi") ||
            text.Contains("bluetooth") ||
            text.Contains("蓝牙");
    }

    private static T SafeGet<T>(Func<T> getValue, T fallback)
    {
        try
        {
            return getValue();
        }
        catch (NetworkInformationException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
