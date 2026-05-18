using System.Net;
using System.Net.Sockets;
using Spectre.Console;
using xidio.Core.Models;

namespace xidio.CLI;

internal static class ConsoleDiagnosticReporter
{
    public static void Print(NetworkDiagnosticReport report)
    {
        PrintTimestamp(report.CollectedAt);
        PrintUserScenario(report.UserScenario);
        PrintNetworkInterfaces(report);
        PrintHostname(report.HostName);
        PrintProxyStatus(report.SystemProxy);
    }

    public static void PrintBanner()
    {
        var appName = new FigletText("XIDIO")
        {
            Color = Color.Purple3,
            Justification = Justify.Center
        };
  
        var version = new Text("v0.1.0", new Style(Color.Purple3))
        {
            Justification = Justify.Center
        };
  
        AnsiConsole.Write(appName);
        AnsiConsole.Write(version);
        Console.WriteLine();
        AnsiConsole.Write(new Text("Xidian Internet Diagnostic Intelligence Operator / 西电校园网诊断工具"){ Justification = Justify.Center });
        AnsiConsole.Write(new Text("由 Project Hazelita 开发 / 以 MIT License 开源"){ Justification = Justify.Center });
        Console.WriteLine("\n\n");
    }

    private static void PrintTimestamp(DateTimeOffset collectedAt)
    {
        AnsiConsole.MarkupLine(
            $"[green]该诊断报告生成于 {collectedAt:yyyy-MM-dd HH:mm:ss}，Unix 时间戳 {collectedAt.ToUnixTimeSeconds()}。[/]");
        Console.WriteLine();
    }

    private static void PrintUserScenario(UserScenarioInfo scenario)
    {
        Console.WriteLine("用户场景信息:");
        Console.WriteLine($"        位置: {scenario.Location}");
        Console.WriteLine($"        连接方式: {FormatConnectionMethod(scenario.ConnectionMethod)}");
        Console.WriteLine($"        主要问题: {FormatProblemSymptom(scenario.ProblemSymptom)}");
        Console.WriteLine($"        影响范围: {FormatImpactScope(scenario.ImpactScope)}");
        Console.WriteLine();
    }

    private static void PrintNetworkInterfaces(NetworkDiagnosticReport report)
    {
        Console.WriteLine($"检测到了 {report.NetworkAdapterDriverInfos.Count} 个物理网络适配器。\n");

        var adapterTable = new Table().RoundedBorder().BorderColor(Color.Grey).Title("物理网络适配器");
        
        adapterTable.AddColumn(new TableColumn("适配器类型").Centered());
        adapterTable.AddColumn(new TableColumn("适配器名称").Centered());
        adapterTable.AddColumn(new TableColumn("驱动版本").Centered());
        
        foreach (var adapter in report.NetworkAdapterDriverInfos)
        {
            adapterTable.AddRow(adapter.Name, adapter.Description, adapter.DriverVersion);
        }
        
        AnsiConsole.Write(adapterTable);

        Console.WriteLine();
        Console.WriteLine(
            $"你的设备上共有 {report.TotalNetworkInterfaceCount} 个网络接口，其中 {report.PrimaryInterfaces.Count} 个是主要网络接口。\n");
        
        var primaryInterfacesTable = new Table().RoundedBorder().BorderColor(Color.Grey).Title("主要网络连接");

        primaryInterfacesTable.AddColumn(new TableColumn("网络类型").Centered());
        primaryInterfacesTable.AddColumn(new TableColumn("描述").Centered());
        primaryInterfacesTable.AddColumn(new TableColumn("状态").Centered());

        foreach (var primaryInterface in report.PrimaryInterfaces)
        {
            primaryInterfacesTable.AddRow(primaryInterface.Kind.ToString(), primaryInterface.Description, primaryInterface.OperationalStatus.ToString());
            
            if (primaryInterface.WirelessConnection is not null)
            {
                Console.WriteLine(
                    $"        SSID: {primaryInterface.WirelessConnection.Ssid} | BSSID: {primaryInterface.WirelessConnection.Bssid} | AP MAC: {primaryInterface.WirelessConnection.ApMac}");
            }
            else if (primaryInterface.Kind == PrimaryInterfaceKind.Wireless &&
                     primaryInterface.NetworkDetails is not null)
            {
                Console.WriteLine("        Wireless connection details are unavailable.");
            }

            if (primaryInterface.NetworkDetails is not null)
                PrintPrimaryInterfaceNetworkDetails(primaryInterface.NetworkDetails);
        }
        
        AnsiConsole.Write(primaryInterfacesTable);
    }

    private static void PrintPrimaryInterfaceNetworkDetails(InterfaceNetworkDetails details)
    {
        Console.WriteLine($"        IPv4 Address(es): {FormatIpv4UnicastAddresses(details.IPv4Addresses)}");
        Console.WriteLine($"        IPv6 Address(es): {FormatIpv6UnicastAddresses(details.IPv6Addresses)}");
        Console.WriteLine($"        Default Gateway(s): {FormatIpAddresses(details.DefaultGateways)}");
        Console.WriteLine($"        DHCP Server(s): {FormatIpAddresses(details.DhcpServers)}");
        Console.WriteLine($"        DNS Server(s): {FormatIpAddresses(details.DnsServers)}");
        Console.WriteLine(
            $"        Interface Metric: {FormatInterfaceMetrics(details.InterfaceMetrics, details.Routes)}");
        Console.WriteLine("        Route Table Summary:");
        PrintRouteFamilySummary("IPv4", details.Routes, (int)AddressFamily.InterNetwork);
        PrintRouteFamilySummary("IPv6", details.Routes, (int)AddressFamily.InterNetworkV6);
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
            })
            .ToList();

        return formatted.Count == 0 ? "<none>" : string.Join(", ", formatted);
    }

    private static string FormatIpv6UnicastAddresses(IReadOnlyList<InterfaceIpAddressInfo> addresses)
    {
        var formatted = addresses
            .Select(addressInfo => $"{addressInfo.Address}/{addressInfo.PrefixLength}")
            .ToList();

        return formatted.Count == 0 ? "<none>" : string.Join(", ", formatted);
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

        return formattedMetrics.Count == 0 ? "<unavailable>" : string.Join(", ", formattedMetrics);
    }

    private static void PrintRouteFamilySummary(
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

    private static void PrintHostname(string hostName)
    {
        Console.WriteLine($"Hostname: {hostName}");
    }

    private static void PrintProxyStatus(SystemProxyInfo systemProxy)
    {
        if (systemProxy.IsEnabled)
            Console.WriteLine($"System Proxy is on. The proxy URL is {systemProxy.ProxyUri}.");
        else
            Console.WriteLine("System Proxy is off.");
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

    private static string FormatProblemSymptom(ProblemSymptom problemSymptom)
    {
        return problemSymptom switch
        {
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

    private static string FormatImpactScope(ImpactScope impactScope)
    {
        return impactScope switch
        {
            ImpactScope.OnlyThisDevice => "只有这台设备",
            ImpactScope.SameRoomOrDormitory => "同宿舍 / 同房间也有人遇到",
            ImpactScope.SameFloorOrArea => "同楼层 / 附近区域也有人遇到",
            ImpactScope.WiderArea => "更大范围都有人遇到",
            _ => "不清楚"
        };
    }
}
