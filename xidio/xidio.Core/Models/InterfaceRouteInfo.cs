namespace xidio.Core.Models;

public sealed class InterfaceRouteInfo
{
    public required string DestinationPrefix { get; init; }

    public required string NextHop { get; init; }

    public required int AddressFamily { get; init; }

    public required int RouteMetric { get; init; }

    public required int InterfaceMetric { get; init; }

    public int TotalMetric => RouteMetric + InterfaceMetric;
}
