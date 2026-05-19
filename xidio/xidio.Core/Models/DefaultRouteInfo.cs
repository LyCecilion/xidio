namespace xidio.Core.Models;

public sealed class DefaultRouteInfo
{
    public required string DestinationPrefix { get; init; }

    public required string NextHop { get; init; }

    public required int AddressFamily { get; init; }

    public required int InterfaceIndex { get; init; }

    public required string InterfaceAlias { get; init; }

    public required int RouteMetric { get; init; }

    public required int InterfaceMetric { get; init; }

    public int TotalMetric => RouteMetric + InterfaceMetric;
}
