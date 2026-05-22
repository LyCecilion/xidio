namespace xidio.Core.Models;

public sealed class NetworkDiagnosticProgress
{
    public required NetworkDiagnosticStage Stage { get; init; }

    public required string Message { get; init; }

    public required int CompletedSteps { get; init; }

    public required int TotalSteps { get; init; }

    public double Percentage => TotalSteps <= 0 ? 0 : CompletedSteps * 100d / TotalSteps;
}
