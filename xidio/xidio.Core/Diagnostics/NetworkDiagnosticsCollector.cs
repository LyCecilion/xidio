using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Core.Diagnostics;

public sealed class NetworkDiagnosticsCollector(IPlatformNetworkDiagnosticsProvider? platformDiagnostics = null)
{
    private const int TotalProgressSteps = 6;
    private readonly IPlatformNetworkDiagnosticsProvider _platformDiagnostics = platformDiagnostics ?? NoopPlatformNetworkDiagnosticsProvider.Instance;

    public NetworkDiagnosticReport Collect()
    {
        return Collect(UserScenarioInfo.Unspecified);
    }

    public NetworkDiagnosticReport Collect(UserScenarioInfo userScenario)
    {
        return CollectAsync(userScenario).GetAwaiter().GetResult();
    }

    public Task<NetworkDiagnosticReport> CollectAsync(
        IProgress<NetworkDiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return CollectAsync(UserScenarioInfo.Unspecified, progress, cancellationToken);
    }

    public async Task<NetworkDiagnosticReport> CollectAsync(
        UserScenarioInfo userScenario,
        IProgress<NetworkDiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(progress, NetworkDiagnosticStage.Starting, "正在准备诊断环境", 0);

        var collectedAt = DateTimeOffset.Now;
        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        ReportProgress(progress, NetworkDiagnosticStage.NetworkInterfaces, "已读取系统网络接口", 1);

        var physicalInterfaceIds = await SafeGetAsync(
            ct => _platformDiagnostics.GetPhysicalNetworkInterfaceIdsAsync(ct),
            Array.Empty<string>(),
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.PhysicalAdapters, "已识别物理网络适配器", 2);

        var adapterDriverInfos = await SafeGetAsync(
            ct => _platformDiagnostics.GetNetworkAdapterDriverInfosAsync(ct),
            Array.Empty<NetworkAdapterDriverInfo>(),
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.DriverInfo, "已读取网卡驱动信息", 3);

        var primaryInterfaces = await GetPrimaryInterfacesAsync(
            allInterfaces,
            physicalInterfaceIds,
            progress,
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.PrimaryInterfaces, "已完成主要网络接口采集", 4);

        var systemProxy = SafeGet(GetSystemProxyInfo, new SystemProxyInfo { IsEnabled = false });
        ReportProgress(progress, NetworkDiagnosticStage.SystemProxy, "已读取系统代理状态", 5);

        var report = new NetworkDiagnosticReport
        {
            CollectedAt = collectedAt,
            TotalNetworkInterfaceCount = allInterfaces.Length,
            HostName = SafeGet(Dns.GetHostName, "<unknown>"),
            UserScenario = userScenario,
            SystemProxy = systemProxy,
            NetworkAdapterDriverInfos = adapterDriverInfos,
            PrimaryInterfaces = primaryInterfaces
        };

        ReportProgress(progress, NetworkDiagnosticStage.Completed, "诊断信息采集完成", TotalProgressSteps);
        return report;
    }

    private async Task<IReadOnlyList<PrimaryInterfaceInfo>> GetPrimaryInterfacesAsync(
        IEnumerable<NetworkInterface> networkInterfaces,
        IReadOnlyCollection<string> physicalInterfaceIds,
        IProgress<NetworkDiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new List<PrimaryInterfaceInfo>();
        var hasPlatformPhysicalIds = physicalInterfaceIds.Count > 0;

        foreach (var networkInterface in networkInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = ClassifyPrimaryInterface(networkInterface, physicalInterfaceIds, hasPlatformPhysicalIds);
            if (kind is null)
                continue;

            var isUp = networkInterface.OperationalStatus == OperationalStatus.Up;
            WirelessConnectionInfo? wirelessInfo = null;
            InterfaceNetworkDetails? networkDetails = null;

            if (isUp && kind == PrimaryInterfaceKind.Wireless)
            {
                ReportProgress(
                    progress,
                    NetworkDiagnosticStage.WirelessInfo,
                    $"正在读取无线连接信息：{networkInterface.Name}",
                    3);

                wirelessInfo = await SafeGetAsync(
                    ct => _platformDiagnostics.GetWirelessConnectionInfoAsync(networkInterface, ct),
                    null,
                    cancellationToken);
            }

            if (isUp)
            {
                ReportProgress(
                    progress,
                    NetworkDiagnosticStage.NetworkDetails,
                    $"正在读取网络配置：{networkInterface.Name}",
                    3);

                networkDetails = await GetNetworkDetailsAsync(networkInterface, cancellationToken);
            }

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

    private static PrimaryInterfaceKind? ClassifyPrimaryInterface(
        NetworkInterface networkInterface,
        IReadOnlyCollection<string> physicalInterfaceIds,
        bool hasPlatformPhysicalIds)
    {
        var type = networkInterface.NetworkInterfaceType;

        // 判断为 Ppp 后进一步判断是不是 Pppoe
        if (type == NetworkInterfaceType.Ppp)
            return LooksLikeRealPppInterface(networkInterface) ? PrimaryInterfaceKind.Pppoe : null;

        // 去掉 Loopback, Tunnel, Unknown 等接口
        if (!IsEthernetType(type) && type != NetworkInterfaceType.Wireless80211)
            return null;

        // 去掉虚拟接口
        if (LooksLikeVirtualAdapter(networkInterface.Name, networkInterface.Description))
            return null;

        // 判断物理接口（物理接口 ID 列表）
        if (hasPlatformPhysicalIds && !physicalInterfaceIds.Contains(NormalizeGuid(networkInterface.Id)))
            return null;

        // 最终判断是否是 Wireless80211
        if (type == NetworkInterfaceType.Wireless80211)
            return PrimaryInterfaceKind.Wireless;

        // 最终排除蓝牙，标记为 Ethernet
        return LooksLikeBluetooth(networkInterface.Name, networkInterface.Description)
            ? null
            : PrimaryInterfaceKind.Ethernet;
    }

    private async Task<InterfaceNetworkDetails?> GetNetworkDetailsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        IPInterfaceProperties properties;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            properties = networkInterface.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        var routes = await SafeGetAsync(
            ct => _platformDiagnostics.GetRoutesAsync(networkInterface, ct),
            Array.Empty<InterfaceRouteInfo>(),
            cancellationToken);
        var metrics = await SafeGetAsync(
            ct => _platformDiagnostics.GetInterfaceMetricsAsync(networkInterface, ct),
            Array.Empty<InterfaceMetricInfo>(),
            cancellationToken);

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
        using var handler = new HttpClientHandler();
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

    private static readonly string[] VirtualAdapterKeywords =
    [
        "vmware",
        "vmnet",
        "hyper-v",
        "hyperv",
        "vswitch",
        "vethernet",
        "virtualbox",
        "host-only",
        "wsl",
        "tap-windows",
        "tap adapter",
        "tun adapter",
        "wireguard",
        "tailscale",
        "zerotier",
        "npcap",
        "winpcap",
        "packet driver",
        "wi-fi direct",
        "wifi direct",
        "virtual wifi",
        "bluetooth",
        "蓝牙",
        "filter",
        "wan miniport",
        "qos",
        "packet scheduler",
        "kernel debug",
        "wfp"
    ];

    private static bool MatchesVirtualKeywords(string text)
    {
        return VirtualAdapterKeywords.Any(text.Contains);
    }

    private static bool LooksLikeRealPppInterface(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
        return !MatchesVirtualKeywords(text);
    }

    private static bool LooksLikeVirtualAdapter(string name, string description)
    {
        var text = $"{name} {description}".ToLowerInvariant();
        return MatchesVirtualKeywords(text);
    }

    private static void ReportProgress(
        IProgress<NetworkDiagnosticProgress>? progress,
        NetworkDiagnosticStage stage,
        string message,
        int completedSteps)
    {
        progress?.Report(new NetworkDiagnosticProgress
        {
            Stage = stage,
            Message = message,
            CompletedSteps = completedSteps,
            TotalSteps = TotalProgressSteps
        });
    }

    private static async ValueTask<T> SafeGetAsync<T>(
        Func<CancellationToken, ValueTask<T>> getValue,
        T fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await getValue(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static T SafeGet<T>(Func<T> getValue, T fallback)
    {
        try
        {
            return getValue();
        }
        catch (OperationCanceledException)
        {
            throw;
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
