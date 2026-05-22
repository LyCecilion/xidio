using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Globalization;
using Spectre.Console;
using xidio.Core.Diagnostics;
using xidio.Core.Models;

namespace xidio.CLI;

internal static class ConsoleDiagnosticReporter
{
    public static void Print(NetworkDiagnosticReport report)
    {
        AnsiConsole.WriteLine();
        PrintReportSummary(report);
        PrintSystemInfo(report.OperatingSystem);
        PrintClockInfo(report.Clock);
        PrintUserScenario(report.UserScenario);
        PrintDefaultRoutes(report.DefaultRoutes);
        PrintNetworkInterfaces(report);
        PrintPppoeInfo(report.Pppoe);
        PrintProbeResults(report.ProbeResults);
    }

    public static void PrintBanner()
    {
        AnsiConsole.MarkupLine(
            $"[purple]Welcome to xidio.CLI v{Markup.Escape(XidioVersion.InformationalVersion)}, Xidian Internet Diagnostic Intelligence Operator.[/]");
        AnsiConsole.MarkupLine("[purple]This project is developed by Project Hazelita, and uses the MIT license.[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintReportSummary(NetworkDiagnosticReport report)
    {
        PrintSection("报告概要");

        var summaryTable = CreateKeyValueTable();
        summaryTable.AddRow("报告生成时间", Escape($"{report.CollectedAt:yyyy-MM-dd HH:mm:ss}"));
        summaryTable.AddRow("报告生成 Unix 时间戳", FormatInvariant(report.CollectedAt.ToUnixTimeSeconds()));
        summaryTable.AddRow("主机名", Escape(report.HostName));
        summaryTable.AddRow("系统代理", FormatProxyStatus(report.SystemProxy));
        summaryTable.AddRow("网络接口总数", FormatInvariant(report.TotalNetworkInterfaceCount));
        summaryTable.AddRow("主要网络接口个数", FormatInvariant(report.PrimaryInterfaces.Count));
        summaryTable.AddRow("物理适配器个数", FormatInvariant(report.NetworkAdapterDriverInfos.Count));

        AnsiConsole.Write(summaryTable);
    }

    private static void PrintSystemInfo(OperatingSystemDiagnosticInfo operatingSystem)
    {
        PrintSection("系统信息");

        var systemTable = CreateKeyValueTable();
        systemTable.AddRow("操作系统 / OS", Escape(operatingSystem.Family));
        systemTable.AddRow("描述", Escape(operatingSystem.Description));
        systemTable.AddRow("版本", Escape(operatingSystem.Version));
        systemTable.AddRow("架构", Escape(operatingSystem.Architecture));
        systemTable.AddRow("xidio 版本", Escape(operatingSystem.XidioVersion));
        systemTable.AddRow("是否提升权限", FormatNullableBool(operatingSystem.IsElevated));

        AnsiConsole.Write(systemTable);
    }

    private static void PrintClockInfo(ClockDiagnosticInfo clock)
    {
        PrintSection("时间校验");

        var clockTable = CreateKeyValueTable();
        clockTable.AddRow("本机时间", Escape($"{clock.LocalTime:yyyy-MM-dd HH:mm:ss zzz}"));
        clockTable.AddRow("NTP 服务器", Escape(clock.NtpServer));
        clockTable.AddRow("NTP 时间", clock.NtpTime is null
            ? "[grey]<unavailable>[/]"
            : Escape($"{clock.NtpTime:yyyy-MM-dd HH:mm:ss zzz}"));
        clockTable.AddRow("时间偏差", clock.Offset is null
            ? "[grey]<unavailable>[/]"
            : Escape(FormatTimeSpan(clock.Offset.Value)));
        clockTable.AddRow("错误", string.IsNullOrWhiteSpace(clock.Error)
            ? "[grey]<none>[/]"
            : Escape(clock.Error));

        AnsiConsole.Write(clockTable);
    }

    private static void PrintUserScenario(UserScenarioInfo scenario)
    {
        PrintSection("用户场景");

        var scenarioTable = CreateKeyValueTable();
        scenarioTable.AddRow("位置", Escape(scenario.Location));
        scenarioTable.AddRow("连接方式", Escape(FormatConnectionMethod(scenario.ConnectionMethod)));
        scenarioTable.AddRow("接入方式", Escape(FormatNetworkAccessMethod(scenario.AccessMethod)));
        scenarioTable.AddRow("主要问题", Escape(FormatProblemSymptom(scenario.ProblemSymptom)));
        scenarioTable.AddRow("影响范围", Escape(FormatImpactScope(scenario.ImpactScope, scenario.ProblemSymptom)));

        AnsiConsole.Write(scenarioTable);
    }

    private static void PrintDefaultRoutes(IReadOnlyList<DefaultRouteInfo> defaultRoutes)
    {
        PrintSection("默认路由");

        if (defaultRoutes.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]未获取到默认路由信息。[/]");
            return;
        }

        var routeTable = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();

        routeTable.AddColumn("协议");
        routeTable.AddColumn("下一跳");
        routeTable.AddColumn("接口");
        routeTable.AddColumn("Metric");

        foreach (var route in defaultRoutes)
        {
            routeTable.AddRow(
                Escape(FormatAddressFamily(route.AddressFamily)),
                Escape(FormatNextHop(route.NextHop)),
                Escape(string.IsNullOrWhiteSpace(route.InterfaceAlias)
                    ? $"ifIndex {route.InterfaceIndex}"
                    : $"{route.InterfaceAlias} (ifIndex {route.InterfaceIndex})"),
                Escape($"route {route.RouteMetric}, interface {route.InterfaceMetric}, total {route.TotalMetric}"));
        }

        AnsiConsole.Write(routeTable);
    }

    private static void PrintNetworkInterfaces(NetworkDiagnosticReport report)
    {
        PrintSection("物理网络适配器");
        PrintAdapterDriverInfos(report.NetworkAdapterDriverInfos);

        PrintSection("主要网络连接");
        PrintPrimaryInterfaces(report.PrimaryInterfaces);
    }

    private static void PrintPppoeInfo(PppoeDiagnosticInfo pppoe)
    {
        PrintSection("PPPoE 概要");

        var pppoeTable = CreateKeyValueTable();
        pppoeTable.AddRow("检测到 PPPoE 接口", FormatBool(pppoe.HasPppoeInterface));
        pppoeTable.AddRow("PPPoE 接口名称", FormatStringList(pppoe.InterfaceNames));
        pppoeTable.AddRow("PPPoE 默认路由", FormatBool(pppoe.HasDefaultRoute));

        AnsiConsole.Write(pppoeTable);
    }

    private static void PrintProbeResults(IReadOnlyList<TargetProbeResult> probeResults)
    {
        PrintSection("主动探测");

        if (probeResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]未执行主动探测。[/]");
            return;
        }

        foreach (var result in probeResults)
            PrintProbeResult(result);
    }

    private static void PrintProbeResult(TargetProbeResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]{Escape(result.Target.Name)}[/] [grey]{Escape(result.Target.Host)}[/]");

        var probeTable = new Table()
            .SimpleBorder()
            .BorderColor(Color.Grey)
            .Expand();

        probeTable.AddColumn("项目");
        probeTable.AddColumn("结果");
        probeTable.AddColumn("详情");

        foreach (var dnsResult in result.SystemDnsResults)
            probeTable.AddRow($"系统 DNS {dnsResult.QueryType}", FormatSuccess(dnsResult.Succeeded), FormatDnsResult(dnsResult));

        foreach (var dnsResult in result.DirectDnsResults)
            probeTable.AddRow($"直连 DNS {dnsResult.QueryType}", FormatSuccess(dnsResult.Succeeded), FormatDnsResult(dnsResult));

        probeTable.AddRow("ICMP Ping", FormatSuccess(result.Ping.Succeeded), FormatPingResult(result.Ping));

        foreach (var tcpResult in result.TcpConnectResults)
            probeTable.AddRow($"TCP {tcpResult.Port}", FormatSuccess(tcpResult.Succeeded), FormatTcpResult(tcpResult));

        if (result.Http is not null)
            probeTable.AddRow("HTTP GET", FormatSuccess(result.Http.Succeeded), FormatHttpResult(result.Http));

        if (result.Https is not null)
            probeTable.AddRow("HTTPS GET", FormatSuccess(result.Https.Succeeded), FormatHttpResult(result.Https));

        AnsiConsole.Write(probeTable);
    }

    private static void PrintAdapterDriverInfos(IReadOnlyList<NetworkAdapterDriverInfo> adapters)
    {
        if (adapters.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]未获取到物理网络适配器驱动信息。[/]");
            return;
        }

        var adapterTable = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();

        adapterTable.AddColumn(new TableColumn("名称"));
        adapterTable.AddColumn(new TableColumn("描述"));
        adapterTable.AddColumn(new TableColumn("驱动版本"));

        foreach (var adapter in adapters)
        {
            adapterTable.AddRow(
                Escape(adapter.Name),
                Escape(adapter.Description),
                Escape(adapter.DriverVersion));
        }

        AnsiConsole.Write(adapterTable);
    }

    private static void PrintPrimaryInterfaces(IReadOnlyList<PrimaryInterfaceInfo> primaryInterfaces)
    {
        if (primaryInterfaces.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]未识别到主要网络接口。[/]");
            return;
        }

        var overviewTable = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();

        overviewTable.AddColumn(new TableColumn("类型"));
        overviewTable.AddColumn(new TableColumn("名称"));
        overviewTable.AddColumn(new TableColumn("启用"));
        overviewTable.AddColumn(new TableColumn("连接"));
        overviewTable.AddColumn(new TableColumn("状态"));
        overviewTable.AddColumn(new TableColumn("链路速度"));

        foreach (var primaryInterface in primaryInterfaces)
        {
            overviewTable.AddRow(
                Escape(FormatPrimaryInterfaceKind(primaryInterface.Kind)),
                Escape(primaryInterface.Name),
                FormatNullableBool(primaryInterface.IsEnabled),
                FormatBool(primaryInterface.IsConnected),
                FormatOperationalStatus(primaryInterface.OperationalStatus),
                Escape(FormatLinkSpeed(primaryInterface.LinkSpeedBitsPerSecond)));
        }

        AnsiConsole.Write(overviewTable);

        foreach (var primaryInterface in primaryInterfaces)
            PrintPrimaryInterfaceDetails(primaryInterface);
    }

    private static void PrintPrimaryInterfaceDetails(PrimaryInterfaceInfo primaryInterface)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]{Escape(FormatPrimaryInterfaceKind(primaryInterface.Kind))}[/] [grey]{Escape(primaryInterface.Name)}[/]");

        var detailsTable = CreateKeyValueTable();
        detailsTable.AddRow("接口 ID", Escape(primaryInterface.Id));
        detailsTable.AddRow("描述", Escape(primaryInterface.Description));
        detailsTable.AddRow("启用", FormatNullableBool(primaryInterface.IsEnabled));
        detailsTable.AddRow("已连接", FormatBool(primaryInterface.IsConnected));
        detailsTable.AddRow("状态", FormatOperationalStatus(primaryInterface.OperationalStatus));
        detailsTable.AddRow("接口类型", Escape(primaryInterface.NetworkInterfaceType.ToString()));
        detailsTable.AddRow("MAC 地址", Escape(primaryInterface.MacAddress ?? "<unavailable>"));
        detailsTable.AddRow("链路速度", Escape(FormatLinkSpeed(primaryInterface.LinkSpeedBitsPerSecond)));
        detailsTable.AddRow("MTU", Escape(primaryInterface.Mtu is null
            ? "<unavailable>"
            : FormatInvariant(primaryInterface.Mtu.Value)));

        if (primaryInterface.WirelessConnection is not null)
        {
            detailsTable.AddRow("SSID", Escape(primaryInterface.WirelessConnection.Ssid));
            detailsTable.AddRow("BSSID", Escape(primaryInterface.WirelessConnection.Bssid));
            detailsTable.AddRow("RSSI", Escape(primaryInterface.WirelessConnection.RssiDbm is null
                ? "<unavailable>"
                : $"{primaryInterface.WirelessConnection.RssiDbm} dBm"));
            detailsTable.AddRow("信号质量", Escape(primaryInterface.WirelessConnection.SignalQualityPercent is null
                ? "<unavailable>"
                : $"{primaryInterface.WirelessConnection.SignalQualityPercent}%"));
            detailsTable.AddRow("频段/信道", Escape(FormatWirelessChannel(primaryInterface.WirelessConnection)));
            detailsTable.AddRow("PHY", Escape(primaryInterface.WirelessConnection.PhyType ?? "<unavailable>"));
            detailsTable.AddRow("认证/加密", Escape(FormatWirelessSecurity(primaryInterface.WirelessConnection)));
            detailsTable.AddRow("当前速率", Escape(FormatWirelessRates(primaryInterface.WirelessConnection)));
        }
        else if (primaryInterface is { Kind: PrimaryInterfaceKind.Wireless, NetworkDetails: not null })
        {
            detailsTable.AddRow("无线连接", "[yellow]未获取到 SSID/BSSID[/]");
        }

        if (primaryInterface.NetworkDetails is null)
        {
            detailsTable.AddRow("网络配置", "[grey]<unavailable>[/]");
            AnsiConsole.Write(detailsTable);
            return;
        }

        var details = primaryInterface.NetworkDetails;
        detailsTable.AddRow("IPv4 Address(es)", FormatIpv4UnicastAddresses(details.IPv4Addresses));
        detailsTable.AddRow("IPv6 Address(es)", FormatIpv6UnicastAddresses(details.IPv6Addresses));
        detailsTable.AddRow("Default Gateway(s)", FormatIpAddresses(details.DefaultGateways));
        detailsTable.AddRow("DHCP Enabled", FormatNullableBool(details.DhcpInfo?.IsEnabled));
        detailsTable.AddRow("DHCP Lease", Escape(FormatDhcpLease(details.DhcpInfo)));
        detailsTable.AddRow("DHCP Server(s)", FormatIpAddresses(details.DhcpServers));
        detailsTable.AddRow("DNS Server(s)", FormatIpAddresses(details.DnsServers));
        detailsTable.AddRow("Interface Metric", FormatInterfaceMetrics(details.InterfaceMetrics, details.Routes));

        AnsiConsole.Write(detailsTable);
        PrintRouteSummary(details.Routes);
        PrintNeighborSummary(details.Neighbors);

        if (primaryInterface.WirelessConnection is not null)
            PrintVisibleWirelessNetworks(primaryInterface.WirelessConnection.VisibleNetworks);
    }

    private static void PrintRouteSummary(IReadOnlyList<InterfaceRouteInfo> routes)
    {
        var routeTable = new Table()
            .SimpleBorder()
            .BorderColor(Color.Grey)
            .Expand();

        routeTable.AddColumn(new TableColumn("协议"));
        routeTable.AddColumn(new TableColumn("路由数"));
        routeTable.AddColumn(new TableColumn("默认路由"));
        routeTable.AddColumn(new TableColumn("样例路由"));

        AddRouteFamilySummary(routeTable, "IPv4", routes, (int)AddressFamily.InterNetwork);
        AddRouteFamilySummary(routeTable, "IPv6", routes, (int)AddressFamily.InterNetworkV6);

        AnsiConsole.Write(routeTable);
    }

    private static void PrintNeighborSummary(IReadOnlyList<NetworkNeighborInfo> neighbors)
    {
        var relevantNeighbors = neighbors
            .Take(8)
            .ToList();

        if (relevantNeighbors.Count == 0)
            return;

        var neighborTable = new Table()
            .SimpleBorder()
            .BorderColor(Color.Grey)
            .Expand();

        neighborTable.AddColumn("协议");
        neighborTable.AddColumn("IP");
        neighborTable.AddColumn("链路层地址");
        neighborTable.AddColumn("状态");

        foreach (var neighbor in relevantNeighbors)
        {
            neighborTable.AddRow(
                Escape(FormatAddressFamily(neighbor.AddressFamily)),
                Escape(neighbor.IpAddress),
                Escape(string.IsNullOrWhiteSpace(neighbor.LinkLayerAddress) ? "<none>" : neighbor.LinkLayerAddress),
                Escape(neighbor.State));
        }

        AnsiConsole.Write(neighborTable);
    }

    private static void PrintVisibleWirelessNetworks(IReadOnlyList<VisibleWirelessNetworkInfo> visibleNetworks)
    {
        if (visibleNetworks.Count == 0)
            return;

        var wirelessTable = new Table()
            .SimpleBorder()
            .BorderColor(Color.Grey)
            .Expand();

        wirelessTable.AddColumn("可见 SSID");
        wirelessTable.AddColumn("信号");
        wirelessTable.AddColumn("认证/加密");
        wirelessTable.AddColumn("BSSID 数");

        foreach (var network in visibleNetworks.Take(12))
        {
            wirelessTable.AddRow(
                Escape(network.Ssid),
                Escape(network.SignalQualityPercent is null ? "<unknown>" : $"{network.SignalQualityPercent}%"),
                Escape($"{network.Authentication ?? "<unknown>"} / {network.Cipher ?? "<unknown>"}"),
                Escape(network.BssidCount is null ? "<unknown>" : FormatInvariant(network.BssidCount.Value)));
        }

        AnsiConsole.Write(wirelessTable);
    }

    private static void AddRouteFamilySummary(
        Table routeTable,
        string label,
        IReadOnlyList<InterfaceRouteInfo> routes,
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
            routeTable.AddRow(label, "0", "[grey]<none>[/]", "[grey]<none>[/]");
            return;
        }

        var defaultRoutes = familyRoutes
            .Where(IsDefaultRoute)
            .Take(3)
            .Select(FormatRoute);
        var sampleRoutes = familyRoutes
            .Where(route => !IsDefaultRoute(route))
            .Take(3)
            .Select(FormatRoute);

        routeTable.AddRow(
            label,
            FormatInvariant(familyRoutes.Count),
            FormatStringList(defaultRoutes),
            FormatStringList(sampleRoutes));
    }

    private static Table CreateKeyValueTable()
    {
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();

        table.AddColumn(new TableColumn("字段"));
        table.AddColumn(new TableColumn("值"));

        return table;
    }

    private static void PrintSection(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold purple]{Escape(title)}[/]");
    }

    private static string FormatIpv4UnicastAddresses(IReadOnlyList<InterfaceIpAddressInfo> addresses)
    {
        var formatted = addresses
            .Select(addressInfo =>
            {
                var mask = addressInfo.IPv4Mask?.ToString();
                var prefix = addressInfo.PrefixLength >= 0 ? $"/{addressInfo.PrefixLength}" : "";

                return string.IsNullOrWhiteSpace(mask)
                    ? $"{addressInfo.Address}{prefix} (subnet mask unavailable)"
                    : $"{addressInfo.Address}{prefix} (subnet mask {mask})";
            });

        return FormatStringList(formatted);
    }

    private static string FormatIpv6UnicastAddresses(IReadOnlyList<InterfaceIpAddressInfo> addresses)
    {
        return FormatStringList(addresses.Select(addressInfo => $"{addressInfo.Address}/{addressInfo.PrefixLength}"));
    }

    private static string FormatIpAddresses(IEnumerable<IPAddress> addresses)
    {
        return FormatStringList(addresses.Select(address => address.ToString()));
    }

    private static string FormatStringList(IEnumerable<string> values)
    {
        var formatted = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return formatted.Count == 0
            ? "[grey]<none>[/]"
            : Escape(string.Join(Environment.NewLine, formatted));
    }

    private static string FormatInterfaceMetrics(
        IReadOnlyList<InterfaceMetricInfo> metrics,
        IReadOnlyList<InterfaceRouteInfo> routes)
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

        return formattedMetrics.Count == 0
            ? "[grey]<unavailable>[/]"
            : Escape(string.Join(Environment.NewLine, formattedMetrics));
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

        return
            $"{route.DestinationPrefix} {nextHop} (route {route.RouteMetric}, interface {route.InterfaceMetric}, total {route.TotalMetric})";
    }

    private static string FormatAddressFamily(int addressFamily)
    {
        return addressFamily == (int)AddressFamily.InterNetwork
            ? "IPv4"
            : addressFamily == (int)AddressFamily.InterNetworkV6
                ? "IPv6"
                : $"AF{addressFamily}";
    }

    private static string FormatProxyStatus(SystemProxyInfo systemProxy)
    {
        return systemProxy.IsEnabled
            ? $"[yellow]on[/] {Escape(systemProxy.ProxyUri?.ToString() ?? "<unknown>")}"
            : "[green]off[/]";
    }

    private static string FormatBool(bool value)
    {
        return value ? "[green]是[/]" : "[yellow]否[/]";
    }

    private static string FormatNullableBool(bool? value)
    {
        return value is null ? "[grey]<unknown>[/]" : FormatBool(value.Value);
    }

    private static string FormatSuccess(bool value)
    {
        return value ? "[green]OK[/]" : "[yellow]FAIL[/]";
    }

    private static string FormatLinkSpeed(long? bitsPerSecond)
    {
        if (bitsPerSecond is null or <= 0)
            return "<unavailable>";

        var mbps = bitsPerSecond.Value / 1_000_000d;
        return mbps >= 1000
            ? $"{mbps / 1000d:0.##} Gbps"
            : $"{mbps:0.##} Mbps";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.TotalMilliseconds switch
        {
            >= 1000 or <= -1000 => $"{timeSpan.TotalSeconds:0.###} s",
            _ => $"{timeSpan.TotalMilliseconds:0.###} ms"
        };
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        return duration is null ? "<unknown>" : FormatTimeSpan(duration.Value);
    }

    private static string FormatNextHop(string nextHop)
    {
        return string.IsNullOrWhiteSpace(nextHop) || nextHop is "0.0.0.0" or "::"
            ? "on-link"
            : nextHop;
    }

    private static string FormatDhcpLease(InterfaceDhcpInfo? dhcpInfo)
    {
        if (dhcpInfo is null)
            return "<unavailable>";

        var obtained = dhcpInfo.LeaseObtained is null
            ? "<unknown>"
            : $"{dhcpInfo.LeaseObtained:yyyy-MM-dd HH:mm:ss}";
        var expires = dhcpInfo.LeaseExpires is null
            ? "<unknown>"
            : $"{dhcpInfo.LeaseExpires:yyyy-MM-dd HH:mm:ss}";

        return $"{obtained} -> {expires}";
    }

    private static string FormatWirelessChannel(WirelessConnectionInfo wireless)
    {
        var frequency = wireless.FrequencyGhz is null ? "<unknown>" : $"{wireless.FrequencyGhz:0.###} GHz";
        var channel = wireless.Channel is null ? "<unknown>" : FormatInvariant(wireless.Channel.Value);

        return $"{frequency}, channel {channel}";
    }

    private static string FormatWirelessSecurity(WirelessConnectionInfo wireless)
    {
        return $"{wireless.Authentication ?? "<unknown>"} / {wireless.Cipher ?? "<unknown>"}";
    }

    private static string FormatWirelessRates(WirelessConnectionInfo wireless)
    {
        var rx = wireless.ReceiveRateMbps is null ? "<unknown>" : $"{wireless.ReceiveRateMbps:0.##} Mbps";
        var tx = wireless.TransmitRateMbps is null ? "<unknown>" : $"{wireless.TransmitRateMbps:0.##} Mbps";

        return $"Rx {rx}, Tx {tx}";
    }

    private static string FormatDnsResult(DnsProbeResult result)
    {
        var source = string.IsNullOrWhiteSpace(result.DnsServer) ? "system" : result.DnsServer;
        var addresses = result.Addresses.Count == 0
            ? "<none>"
            : string.Join(", ", result.Addresses.Select(address => address.ToString()));
        var suffix = string.IsNullOrWhiteSpace(result.Error) ? "" : $"; {result.Error}";

        return Escape($"{source}; {FormatDuration(result.Duration)}; {addresses}{suffix}");
    }

    private static string FormatPingResult(PingProbeResult result)
    {
        var roundtrip = result.RoundtripTimeMilliseconds is null ? "<unknown>" : $"{result.RoundtripTimeMilliseconds} ms";
        var status = result.Status ?? result.Error ?? "<unknown>";

        return Escape($"{status}; {roundtrip}");
    }

    private static string FormatTcpResult(TcpConnectProbeResult result)
    {
        var suffix = string.IsNullOrWhiteSpace(result.Error) ? "" : $"; {result.Error}";
        return Escape($"{FormatDuration(result.Duration)}{suffix}");
    }

    private static string FormatHttpResult(HttpProbeResult result)
    {
        var status = result.StatusCode is null ? "<none>" : FormatInvariant(result.StatusCode.Value);
        var suffix = string.IsNullOrWhiteSpace(result.Error) ? "" : $"; {result.Error}";

        return Escape($"{result.Uri}; status {status}; {FormatDuration(result.Duration)}{suffix}");
    }

    private static string FormatOperationalStatus(OperationalStatus status)
    {
        return status switch
        {
            OperationalStatus.Up => "[green]Up[/]",
            OperationalStatus.Down => "[yellow]Down[/]",
            OperationalStatus.NotPresent => "[grey]NotPresent[/]",
            _ => Escape(status.ToString())
        };
    }

    private static string FormatPrimaryInterfaceKind(PrimaryInterfaceKind kind)
    {
        return kind switch
        {
            PrimaryInterfaceKind.Ethernet => "Ethernet",
            PrimaryInterfaceKind.Wireless => "Wireless",
            PrimaryInterfaceKind.Pppoe => "PPPoE",
            _ => kind.ToString()
        };
    }

    private static string FormatConnectionMethod(ConnectionMethod connectionMethod)
    {
        return connectionMethod switch
        {
            ConnectionMethod.DirectCampusNetwork => "直接连接：校园 Wi-Fi / 宿舍有线 PPPoE",
            ConnectionMethod.CampusNetworkViaRouter => "间接连接：通过路由器等设备",
            ConnectionMethod.OtherNetwork => "其他连接：手机热点 / 校外网络等",
            _ => "不确定"
        };
    }

    private static string FormatNetworkAccessMethod(NetworkAccessMethod accessMethod)
    {
        return accessMethod switch
        {
            NetworkAccessMethod.Wireless => "Wi-Fi",
            NetworkAccessMethod.Ethernet => "有线连接（不拨号）",
            NetworkAccessMethod.Pppoe => "PPPoE / 宽带拨号",
            _ => "不确定"
        };
    }

    private static string FormatProblemSymptom(ProblemSymptom problemSymptom)
    {
        return problemSymptom switch
        {
            ProblemSymptom.NoProblem => "没有问题，只是进行一次诊断",
            ProblemSymptom.CannotConnectWifi => "连不上 Wi-Fi",
            ProblemSymptom.ConnectedNoInternet => "连上了但显示无 Internet",
            ProblemSymptom.CaptivePortalNotShown => "认证页不弹出",
            ProblemSymptom.PortalAuthenticatedNoWeb => "认证成功但打不开网页",
            ProblemSymptom.SomeApplicationsUnavailable => "部分应用能用，部分应用不能用",
            ProblemSymptom.PppoeDialFailed => "有线拨号失败",
            ProblemSymptom.PppoeConnectedNoInternet => "有线拨号成功但没有网",
            ProblemSymptom.Other => "其他问题",
            _ => "不确定"
        };
    }

    private static string FormatImpactScope(ImpactScope impactScope, ProblemSymptom problemSymptom)
    {
        if (problemSymptom == ProblemSymptom.NoProblem)
            return "未询问";

        return impactScope switch
        {
            ImpactScope.OnlyThisDevice => "只有这台设备",
            ImpactScope.SameRoomOrDormitory => "同宿舍 / 同房间也有人遇到",
            ImpactScope.SameFloorOrArea => "同楼层 / 附近区域也有人遇到",
            ImpactScope.WiderArea => "更大范围都有人遇到",
            _ => "不清楚"
        };
    }

    private static string Escape(string value)
    {
        return Markup.Escape(value);
    }

    private static string FormatInvariant(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatInvariant(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
