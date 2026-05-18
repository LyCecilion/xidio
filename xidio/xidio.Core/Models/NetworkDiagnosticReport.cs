namespace xidio.Core.Models;

public sealed class NetworkDiagnosticReport
{
    public required DateTimeOffset CollectedAt { get; init; }

    public required int TotalNetworkInterfaceCount { get; init; }

    public required string HostName { get; init; }

    public required UserScenarioInfo UserScenario { get; init; }

    public required SystemProxyInfo SystemProxy { get; init; }

    public required IReadOnlyList<NetworkAdapterDriverInfo> NetworkAdapterDriverInfos { get; init; }

    public required IReadOnlyList<PrimaryInterfaceInfo> PrimaryInterfaces { get; init; }
}
