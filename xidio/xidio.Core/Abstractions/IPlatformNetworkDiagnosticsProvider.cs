using System.Net.NetworkInformation;
using xidio.Core.Models;

namespace xidio.Core.Abstractions;

public interface IPlatformNetworkDiagnosticsProvider
{
    ValueTask<OperatingSystemDiagnosticInfo?> GetOperatingSystemInfoAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<string>> GetPhysicalNetworkInterfaceIdsAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NetworkAdapterDriverInfo>> GetNetworkAdapterDriverInfosAsync(CancellationToken cancellationToken);

    ValueTask<InterfacePlatformInfo?> GetInterfacePlatformInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<WirelessConnectionInfo?> GetWirelessConnectionInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<InterfaceDhcpInfo?> GetInterfaceDhcpInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<InterfaceMetricInfo>> GetInterfaceMetricsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<InterfaceRouteInfo>> GetRoutesAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NetworkNeighborInfo>> GetNetworkNeighborsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<DefaultRouteInfo>> GetDefaultRoutesAsync(CancellationToken cancellationToken);
}
