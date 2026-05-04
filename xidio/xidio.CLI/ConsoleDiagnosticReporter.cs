using System.Net;
using System.Net.Sockets;
using xidio.Core.Models;

namespace xidio.CLI;

internal static class ConsoleDiagnosticReporter
{
    public static void Print(NetworkDiagnosticReport report)
    {
        PrintBanner();

        Console.WriteLine("Now we're going to collect your device's basic network environment.\n");
        PrintTimestamp(report.CollectedAt);
        PromptLocation();
        PrintNetworkInterfaces(report);
        PrintHostname(report.HostName);
        PrintProxyStatus(report.SystemProxy);
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

                           Early Development build - expect changes and bugs.
                               Built by LyCecilion and xilin, with love.
                                  Be affiliated with Project Hazelita.

                          ====================================================

                          """);
    }

    private static void PrintTimestamp(DateTimeOffset collectedAt)
    {
        Console.WriteLine(
            $"Current time: {collectedAt:yyyy-MM-dd HH:mm:ss}. In Unix timestamp: {collectedAt.ToUnixTimeSeconds()}.\n");
    }

    private static void PromptLocation()
    {
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
    }

    private static void PrintNetworkInterfaces(NetworkDiagnosticReport report)
    {
        Console.WriteLine(
            $"We detected {report.TotalNetworkInterfaceCount} network interface(s), among which {report.PrimaryInterfaces.Count} seem(s) to be primary interfaces.\n");

        Console.WriteLine("Below are detected physical network adapters and driver versions.\n");

        foreach (var adapter in report.NetworkAdapterDriverInfos)
        {
            Console.WriteLine(
                $"    {adapter.Name} | {adapter.Description} | Driver Version: {adapter.DriverVersion}");
        }

        Console.WriteLine();
        Console.WriteLine("Below are detected primary connections.\n");

        foreach (var primaryInterface in report.PrimaryInterfaces)
        {
            Console.WriteLine(
                $"    {primaryInterface.Kind} | {primaryInterface.Name} | {primaryInterface.Description} | {primaryInterface.OperationalStatus}");

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
    }

    private static void PrintPrimaryInterfaceNetworkDetails(InterfaceNetworkDetails details)
    {
        Console.WriteLine($"        IPv4 Address(es): {FormatIpv4UnicastAddresses(details.IPv4Addresses)}");
        Console.WriteLine($"        IPv6 Address(es): {FormatIpv6UnicastAddresses(details.IPv6Addresses)}");
        Console.WriteLine($"        Default Gateway(s): {FormatIpAddresses(details.DefaultGateways)}");
        Console.WriteLine($"        DHCP Server(s): {FormatIpAddresses(details.DhcpServers)}");
        Console.WriteLine($"        DNS Server(s): {FormatIpAddresses(details.DnsServers)}");
        Console.WriteLine($"        Interface Metric: {FormatInterfaceMetrics(details.InterfaceMetrics, details.Routes)}");
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
}
