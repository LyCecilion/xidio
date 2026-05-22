namespace xidio.Core.Models;

public sealed class ProbeTargetInfo
{
    public required string Name { get; init; }

    public required string Host { get; init; }

    public required bool IsCampusTarget { get; init; }
}
