namespace xidio.Core.Models;

public sealed class OperatingSystemDiagnosticInfo
{
    public required string Family { get; init; }

    public required string Description { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required string XidioVersion { get; init; }

    public bool? IsElevated { get; init; }
}
