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

    public ValueTask<IReadOnlyCollection<string>> GetPhysicalNetworkInterfaceIdsAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    public ValueTask<IReadOnlyList<NetworkAdapterDriverInfo>> GetNetworkAdapterDriverInfosAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<NetworkAdapterDriverInfo>>(Array.Empty<NetworkAdapterDriverInfo>());
    }

    public ValueTask<WirelessConnectionInfo?> GetWirelessConnectionInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<WirelessConnectionInfo?>(null);
    }

    public ValueTask<IReadOnlyList<InterfaceMetricInfo>> GetInterfaceMetricsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<InterfaceMetricInfo>>(Array.Empty<InterfaceMetricInfo>());
    }

    public ValueTask<IReadOnlyList<InterfaceRouteInfo>> GetRoutesAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<InterfaceRouteInfo>>(Array.Empty<InterfaceRouteInfo>());
    }
}
