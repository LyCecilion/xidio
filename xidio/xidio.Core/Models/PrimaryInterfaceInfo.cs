using System.Net.NetworkInformation;

namespace xidio.Core.Models;

public sealed class PrimaryInterfaceInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required PrimaryInterfaceKind Kind { get; init; }

    public bool? IsEnabled { get; init; }

    public required bool IsConnected { get; init; }

    public required OperationalStatus OperationalStatus { get; init; }

    public required NetworkInterfaceType NetworkInterfaceType { get; init; }

    public string? MacAddress { get; init; }

    public long? LinkSpeedBitsPerSecond { get; init; }

    public int? Mtu { get; init; }

    public WirelessConnectionInfo? WirelessConnection { get; init; }

    public InterfaceNetworkDetails? NetworkDetails { get; init; }
}
