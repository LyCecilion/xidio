using System.Net.NetworkInformation;
using xidio.Core.Models;

namespace xidio.Core.Abstractions;

public interface IPlatformNetworkDiagnosticsProvider
{
    IReadOnlyCollection<string> GetPhysicalNetworkInterfaceIds();

    IReadOnlyList<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfos();

    WirelessConnectionInfo? GetWirelessConnectionInfo(NetworkInterface networkInterface);

    IReadOnlyList<InterfaceMetricInfo> GetInterfaceMetrics(NetworkInterface networkInterface);

    IReadOnlyList<InterfaceRouteInfo> GetRoutes(NetworkInterface networkInterface);
}
