namespace xidio.Core.Models;

public sealed class NetworkNeighborInfo
{
    public required string IpAddress { get; init; }

    public required string LinkLayerAddress { get; init; }

    public required int AddressFamily { get; init; }

    public required string State { get; init; }
}
