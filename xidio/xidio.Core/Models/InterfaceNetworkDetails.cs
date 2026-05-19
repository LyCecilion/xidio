using System.Net;

namespace xidio.Core.Models;

public sealed class InterfaceNetworkDetails
{
    public required IReadOnlyList<InterfaceIpAddressInfo> IPv4Addresses { get; init; }

    public required IReadOnlyList<InterfaceIpAddressInfo> IPv6Addresses { get; init; }

    public required IReadOnlyList<IPAddress> DefaultGateways { get; init; }

    public required IReadOnlyList<IPAddress> DhcpServers { get; init; }

    public InterfaceDhcpInfo? DhcpInfo { get; init; }

    public required IReadOnlyList<IPAddress> DnsServers { get; init; }

    public required IReadOnlyList<InterfaceMetricInfo> InterfaceMetrics { get; init; }

    public required IReadOnlyList<InterfaceRouteInfo> Routes { get; init; }

    public required IReadOnlyList<NetworkNeighborInfo> Neighbors { get; init; }
}
