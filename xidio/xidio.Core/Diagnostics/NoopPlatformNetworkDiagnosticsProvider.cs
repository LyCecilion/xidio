using System.Net.NetworkInformation;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Core.Diagnostics;

public sealed class NoopPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    public static NoopPlatformNetworkDiagnosticsProvider Instance { get; } = new();

    private NoopPlatformNetworkDiagnosticsProvider()
    {
    }

    public IReadOnlyCollection<string> GetPhysicalNetworkInterfaceIds()
    {
        return Array.Empty<string>();
    }

    public IReadOnlyList<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfos()
    {
        return Array.Empty<NetworkAdapterDriverInfo>();
    }

    public WirelessConnectionInfo? GetWirelessConnectionInfo(NetworkInterface networkInterface)
    {
        return null;
    }

    public IReadOnlyList<InterfaceMetricInfo> GetInterfaceMetrics(NetworkInterface networkInterface)
    {
        return Array.Empty<InterfaceMetricInfo>();
    }

    public IReadOnlyList<InterfaceRouteInfo> GetRoutes(NetworkInterface networkInterface)
    {
        return Array.Empty<InterfaceRouteInfo>();
    }
}
