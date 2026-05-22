namespace xidio.Core.Models;

public sealed class PppoeDiagnosticInfo
{
    public required bool HasPppoeInterface { get; init; }

    public required IReadOnlyList<string> InterfaceNames { get; init; }

    public required bool HasDefaultRoute { get; init; }
}
