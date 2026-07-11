using System.Net;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Core.Diagnostics;

public sealed class NetworkDiagnosticsCollector(IPlatformNetworkDiagnosticsProvider? platformDiagnostics = null)
{
    private const int TotalProgressSteps = 10;
    private const string NtpServer = "ntp.aliyun.com";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly IReadOnlyList<ProbeTargetInfo> ProbeTargets =
    [
        new()
        {
            Name = "西电校园网认证服务器域名",
            Host = "w.xidian.edu.cn",
            IsCampusTarget = true
        },
        new()
        {
            Name = "西电校园网认证服务器 IP",
            Host = "10.255.44.33",
            IsCampusTarget = true
        },
        new()
        {
            Name = "镜雨亭CrystaRin",
            Host = "crystal.stellalyr.ink",
            IsCampusTarget = false
        }
    ];
    private readonly IPlatformNetworkDiagnosticsProvider _platformDiagnostics = platformDiagnostics ?? NoopPlatformNetworkDiagnosticsProvider.Instance;

    [Obsolete("Use CollectAsync instead. Synchronous network diagnostics block a long-running operation and may deadlock in context-bound callers.")]
    public NetworkDiagnosticReport Collect()
    {
        return Task.Run(() => CollectAsync(UserScenarioInfo.Unspecified)).Result;
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
        var operatingSystem = await GetOperatingSystemInfoAsync(cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.SystemInfo, "已读取操作系统信息", 1);

        var clock = await GetClockInfoAsync(cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.Clock, "已完成本机时间校验", 2);

        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        ReportProgress(progress, NetworkDiagnosticStage.NetworkInterfaces, "已读取系统网络接口", 3);

        var physicalInterfaceIds = await SafeGetAsync(
            ct => _platformDiagnostics.GetPhysicalNetworkInterfaceIdsAsync(ct),
            [],
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.PhysicalAdapters, "已识别物理网络适配器", 4);

        var adapterDriverInfos = await SafeGetAsync(
            ct => _platformDiagnostics.GetNetworkAdapterDriverInfosAsync(ct),
            [],
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.DriverInfo, "已读取网卡驱动信息", 5);

        var primaryInterfaces = await GetPrimaryInterfacesAsync(
            allInterfaces,
            physicalInterfaceIds,
            progress,
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.PrimaryInterfaces, "已完成主要网络接口采集", 6);

        var defaultRoutes = await SafeGetAsync(
            ct => _platformDiagnostics.GetDefaultRoutesAsync(ct),
            Array.Empty<DefaultRouteInfo>(),
            cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.DefaultRoutes, "已读取默认路由信息", 7);

        var systemProxy = userScenario.CollectSensitiveSystemInformation
            ? SafeGet(GetSystemProxyInfo, new SystemProxyInfo { WasCollected = true, IsEnabled = false })
            : new SystemProxyInfo { WasCollected = false, IsEnabled = false };
        ReportProgress(
            progress,
            NetworkDiagnosticStage.SystemProxy,
            systemProxy.WasCollected ? "已读取系统代理状态" : "已跳过未授权的系统代理收集",
            8);

        var probeResults = await CollectProbeResultsAsync(primaryInterfaces, progress, cancellationToken);
        ReportProgress(progress, NetworkDiagnosticStage.ActiveProbes, "已完成主动探测", 9);

        var report = new NetworkDiagnosticReport
        {
            CollectedAt = collectedAt,
            TotalNetworkInterfaceCount = allInterfaces.Length,
            HostName = SafeGet(Dns.GetHostName, "<unknown>"),
            OperatingSystem = operatingSystem,
            Clock = clock,
            UserScenario = userScenario,
            SystemProxy = systemProxy,
            NetworkAdapterDriverInfos = adapterDriverInfos,
            DefaultRoutes = defaultRoutes,
            PrimaryInterfaces = primaryInterfaces,
            Pppoe = GetPppoeDiagnosticInfo(primaryInterfaces),
            ProbeResults = probeResults
        };

        ReportProgress(progress, NetworkDiagnosticStage.Completed, "诊断信息采集完成", TotalProgressSteps);
        return report;
    }

    private async Task<OperatingSystemDiagnosticInfo> GetOperatingSystemInfoAsync(CancellationToken cancellationToken)
    {
        var platformInfo = await SafeGetAsync(
            ct => _platformDiagnostics.GetOperatingSystemInfoAsync(ct),
            null,
            cancellationToken);

        return platformInfo ?? new OperatingSystemDiagnosticInfo
        {
            Family = GetOperatingSystemFamily(),
            Description = RuntimeInformation.OSDescription,
            Version = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            XidioVersion = XidioVersion.InformationalVersion,
            IsElevated = null
        };
    }

    private static async Task<ClockDiagnosticInfo> GetClockInfoAsync(CancellationToken cancellationToken)
    {
        var localTime = DateTimeOffset.Now;

        try
        {
            var ntpTime = await QueryNtpTimeAsync(NtpServer, cancellationToken);

            return new ClockDiagnosticInfo
            {
                LocalTime = localTime,
                NtpServer = NtpServer,
                NtpTime = ntpTime,
                Offset = localTime - ntpTime
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ClockDiagnosticInfo
            {
                LocalTime = localTime,
                NtpServer = NtpServer,
                Error = ex.Message
            };
        }
    }

    private static async Task<IReadOnlyList<TargetProbeResult>> CollectProbeResultsAsync(
        IReadOnlyList<PrimaryInterfaceInfo> primaryInterfaces,
        IProgress<NetworkDiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        var dnsServers = primaryInterfaces
            .Select(primaryInterface => primaryInterface.NetworkDetails)
            .Where(details => details is not null)
            .SelectMany(details => details!.DnsServers)
            .Distinct()
            .Take(2)
            .ToList();

        var results = new List<TargetProbeResult>();

        foreach (var target in ProbeTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(progress, NetworkDiagnosticStage.ActiveProbes, $"正在探测：{target.Name}", 8);
            results.Add(await ProbeTargetAsync(target, dnsServers, cancellationToken));
        }

        return results;
    }

    private static async Task<TargetProbeResult> ProbeTargetAsync(
        ProbeTargetInfo target,
        IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        var systemDnsResults = await ProbeSystemDnsAsync(target.Host, cancellationToken);
        var directDnsResults = await ProbeDirectDnsAsync(target.Host, dnsServers, cancellationToken);

        return new TargetProbeResult
        {
            Target = target,
            SystemDnsResults = systemDnsResults,
            DirectDnsResults = directDnsResults,
            Ping = await ProbePingAsync(target.Host, cancellationToken),
            TcpConnectResults =
            [
                await ProbeTcpConnectAsync(target.Host, 80, cancellationToken),
                await ProbeTcpConnectAsync(target.Host, 443, cancellationToken),
                await ProbeTcpConnectAsync(target.Host, 53, cancellationToken)
            ],
            Http = await ProbeHttpAsync(new Uri($"http://{target.Host}/"), cancellationToken),
            Https = await ProbeHttpAsync(new Uri($"https://{target.Host}/"), cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<DnsProbeResult>> ProbeSystemDnsAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return
            [
                new DnsProbeResult
                {
                    QueryName = host,
                    QueryType = ipAddress.AddressFamily == AddressFamily.InterNetwork ? "A" : "AAAA",
                    Succeeded = true,
                    Addresses = [ipAddress]
                }
            ];
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .WaitAsync(ProbeTimeout, cancellationToken);
            stopwatch.Stop();

            return
            [
                CreateSystemDnsResult(host, "A", addresses, AddressFamily.InterNetwork, stopwatch.Elapsed, null),
                CreateSystemDnsResult(host, "AAAA", addresses, AddressFamily.InterNetworkV6, stopwatch.Elapsed, null)
            ];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return
            [
                CreateSystemDnsResult(host, "A", [], AddressFamily.InterNetwork, stopwatch.Elapsed, ex.Message),
                CreateSystemDnsResult(host, "AAAA", [], AddressFamily.InterNetworkV6, stopwatch.Elapsed, ex.Message)
            ];
        }
    }

    private static DnsProbeResult CreateSystemDnsResult(
        string host,
        string queryType,
        IEnumerable<IPAddress> addresses,
        AddressFamily addressFamily,
        TimeSpan duration,
        string? error)
    {
        var filteredAddresses = addresses
            .Where(address => address.AddressFamily == addressFamily)
            .ToList();

        return new DnsProbeResult
        {
            QueryName = host,
            QueryType = queryType,
            Succeeded = error is null && filteredAddresses.Count > 0,
            Addresses = filteredAddresses,
            Duration = duration,
            Error = error
        };
    }

    private static async Task<IReadOnlyList<DnsProbeResult>> ProbeDirectDnsAsync(
        string host,
        IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out _) || dnsServers.Count == 0)
            return [];

        var results = new List<DnsProbeResult>();

        foreach (var dnsServer in dnsServers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProbeDirectDnsAsync(host, "A", 1, dnsServer, cancellationToken));
            results.Add(await ProbeDirectDnsAsync(host, "AAAA", 28, dnsServer, cancellationToken));
        }

        return results;
    }

    private static async Task<DnsProbeResult> ProbeDirectDnsAsync(
        string host,
        string queryType,
        ushort queryTypeValue,
        IPAddress dnsServer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var query = BuildDnsQuery(host, queryTypeValue);
            using var udpClient = new UdpClient(dnsServer.AddressFamily);

            await udpClient.SendAsync(query, query.Length, new IPEndPoint(dnsServer, 53))
                .WaitAsync(ProbeTimeout, cancellationToken);

            var response = await udpClient.ReceiveAsync(cancellationToken)
                .AsTask()
                .WaitAsync(ProbeTimeout, cancellationToken);
            stopwatch.Stop();

            var addresses = ParseDnsAddresses(response.Buffer, queryTypeValue);

            return new DnsProbeResult
            {
                QueryName = host,
                QueryType = queryType,
                DnsServer = dnsServer.ToString(),
                Succeeded = addresses.Count > 0,
                Addresses = addresses,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new DnsProbeResult
            {
                QueryName = host,
                QueryType = queryType,
                DnsServer = dnsServer.ToString(),
                Succeeded = false,
                Addresses = [],
                Duration = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
    }

    private static async Task<PingProbeResult> ProbePingAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, (int)ProbeTimeout.TotalMilliseconds)
                .WaitAsync(ProbeTimeout + TimeSpan.FromMilliseconds(250), cancellationToken);

            return new PingProbeResult
            {
                Succeeded = reply.Status == IPStatus.Success,
                RoundtripTimeMilliseconds = reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                Status = reply.Status.ToString()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PingProbeResult
            {
                Succeeded = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<TcpConnectProbeResult> ProbeTcpConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, cancellationToken)
                .AsTask()
                .WaitAsync(ProbeTimeout, cancellationToken);
            stopwatch.Stop();

            return new TcpConnectProbeResult
            {
                Port = port,
                Succeeded = true,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new TcpConnectProbeResult
            {
                Port = port,
                Succeeded = false,
                Duration = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
    }

    private static async Task<HttpProbeResult> ProbeHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };
            using var httpClient = new HttpClient(handler)
            {
                Timeout = ProbeTimeout
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            stopwatch.Stop();

            return new HttpProbeResult
            {
                Uri = uri,
                Succeeded = true,
                StatusCode = (int)response.StatusCode,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return new HttpProbeResult
            {
                Uri = uri,
                Succeeded = false,
                Duration = stopwatch.Elapsed,
                Error = "Timeout"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new HttpProbeResult
            {
                Uri = uri,
                Succeeded = false,
                Duration = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
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
            var platformInfo = await SafeGetAsync(
                ct => _platformDiagnostics.GetInterfacePlatformInfoAsync(networkInterface, ct),
                null,
                cancellationToken);
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
                IsEnabled = platformInfo?.IsEnabled,
                IsConnected = isUp,
                OperationalStatus = networkInterface.OperationalStatus,
                NetworkInterfaceType = networkInterface.NetworkInterfaceType,
                MacAddress = FormatPhysicalAddress(networkInterface.GetPhysicalAddress()),
                LinkSpeedBitsPerSecond = networkInterface.Speed > 0 ? networkInterface.Speed : null,
                Mtu = platformInfo?.Mtu,
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
        var dhcpInfo = await SafeGetAsync(
            ct => _platformDiagnostics.GetInterfaceDhcpInfoAsync(networkInterface, ct),
            null,
            cancellationToken);
        var neighbors = await SafeGetAsync(
            ct => _platformDiagnostics.GetNetworkNeighborsAsync(networkInterface, ct),
            Array.Empty<NetworkNeighborInfo>(),
            cancellationToken);

        return new InterfaceNetworkDetails
        {
            IPv4Addresses = GetUnicastAddresses(properties, AddressFamily.InterNetwork),
            IPv6Addresses = GetUnicastAddresses(properties, AddressFamily.InterNetworkV6),
            DefaultGateways = properties.GatewayAddresses.Select(gateway => gateway.Address).ToList(),
            DhcpServers = GetDhcpServerAddresses(properties),
            DhcpInfo = dhcpInfo,
            DnsServers = properties.DnsAddresses.ToList(),
            InterfaceMetrics = metrics,
            Routes = routes,
            Neighbors = neighbors
        };
    }

    private static IReadOnlyList<IPAddress> GetDhcpServerAddresses(IPInterfaceProperties properties)
    {
        return OperatingSystem.IsMacOS()
            ? Array.Empty<IPAddress>()
            : properties.DhcpServerAddresses.ToList();
    }

    private static async Task<DateTimeOffset> QueryNtpTimeAsync(string server, CancellationToken cancellationToken)
    {
        var request = new byte[48];
        request[0] = 0x1B;

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        await udpClient.SendAsync(request, request.Length, server, 123)
            .WaitAsync(ProbeTimeout, cancellationToken);

        var response = await udpClient.ReceiveAsync(cancellationToken)
            .AsTask()
            .WaitAsync(ProbeTimeout, cancellationToken);

        if (response.Buffer.Length < 48)
            throw new InvalidOperationException("NTP response is too short.");

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(response.Buffer.AsSpan(40, 4));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(response.Buffer.AsSpan(44, 4));
        var milliseconds = seconds * 1000d + fraction * 1000d / uint.MaxValue;
        var ntpEpoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return ntpEpoch.AddMilliseconds(milliseconds).ToLocalTime();
    }

    private static byte[] BuildDnsQuery(string host, ushort queryType)
    {
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var stream = new MemoryStream();

        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..2], id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..4], 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..6], 1);
        stream.Write(header);

        foreach (var label in labels)
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);

            if (labelBytes.Length is 0 or > 63)
                throw new InvalidOperationException("Invalid DNS label length.");

            stream.WriteByte((byte)labelBytes.Length);
            stream.Write(labelBytes);
        }

        stream.WriteByte(0);

        Span<byte> footer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(footer[0..2], queryType);
        BinaryPrimitives.WriteUInt16BigEndian(footer[2..4], 1);
        stream.Write(footer);

        return stream.ToArray();
    }

    private static IReadOnlyList<IPAddress> ParseDnsAddresses(byte[] response, ushort expectedQueryType)
    {
        if (response.Length < 12)
            return Array.Empty<IPAddress>();

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6, 2));
        var offset = 12;

        for (var i = 0; i < questionCount; i++)
        {
            offset = SkipDnsName(response, offset);
            offset += 4;

            if (offset > response.Length)
                return Array.Empty<IPAddress>();
        }

        var addresses = new List<IPAddress>();

        for (var i = 0; i < answerCount; i++)
        {
            offset = SkipDnsName(response, offset);

            if (offset + 10 > response.Length)
                break;

            var type = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 2, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 8, 2));
            offset += 10;

            if (offset + dataLength > response.Length)
                break;

            if (recordClass == 1 && type == expectedQueryType &&
                ((type == 1 && dataLength == 4) || (type == 28 && dataLength == 16)))
            {
                addresses.Add(new IPAddress(response.AsSpan(offset, dataLength).ToArray()));
            }

            offset += dataLength;
        }

        return addresses;
    }

    private static int SkipDnsName(byte[] response, int offset)
    {
        while (offset < response.Length)
        {
            var length = response[offset];

            if (length == 0)
                return offset + 1;

            if ((length & 0xC0) == 0xC0)
                return offset + 2;

            offset += length + 1;
        }

        return response.Length;
    }

    private static List<InterfaceIpAddressInfo> GetUnicastAddresses(
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

    private static PppoeDiagnosticInfo GetPppoeDiagnosticInfo(IReadOnlyList<PrimaryInterfaceInfo> primaryInterfaces)
    {
        var pppoeInterfaces = primaryInterfaces
            .Where(primaryInterface => primaryInterface.Kind == PrimaryInterfaceKind.Pppoe)
            .ToList();

        return new PppoeDiagnosticInfo
        {
            HasPppoeInterface = pppoeInterfaces.Count > 0,
            InterfaceNames = pppoeInterfaces.Select(primaryInterface => primaryInterface.Name).ToList(),
            HasDefaultRoute = pppoeInterfaces.Any(primaryInterface =>
                primaryInterface.NetworkDetails?.Routes.Any(IsDefaultRoute) == true)
        };
    }

    private static bool IsDefaultRoute(InterfaceRouteInfo route)
    {
        return route.DestinationPrefix is "0.0.0.0/0" or "::/0";
    }

    private static SystemProxyInfo GetSystemProxyInfo()
    {
        var targetUri = new Uri("https://xidio.stellalyr.ink");
        using var handler = new HttpClientHandler();
        var defaultProxy = handler.Proxy ?? WebRequest.GetSystemWebProxy();
        var proxyUri = defaultProxy.GetProxy(targetUri);
        var isEnabled = proxyUri is not null && proxyUri != targetUri;

        return new SystemProxyInfo
        {
            WasCollected = true,
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

    private static string? FormatPhysicalAddress(PhysicalAddress physicalAddress)
    {
        var bytes = physicalAddress.GetAddressBytes();

        return bytes.Length == 0
            ? null
            : string.Join(":", bytes.Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static string GetOperatingSystemFamily()
    {
        if (OperatingSystem.IsWindows())
            return "Windows";
        if (OperatingSystem.IsMacOS())
            return "macOS";
        if (OperatingSystem.IsLinux())
            return "Linux";

        return "Unknown";
    }

    private static bool LooksLikeBluetooth(string name, string descriptionOrPnpId)
    {
        var text = $"{name} {descriptionOrPnpId}".ToLowerInvariant();
        return text.Contains("bluetooth")
            || text.Contains("蓝牙")
            || text.StartsWith("bth\\", StringComparison.Ordinal);
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
