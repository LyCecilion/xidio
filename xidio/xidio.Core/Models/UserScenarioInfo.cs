namespace xidio.Core.Models;

public sealed class UserScenarioInfo
{
    public static UserScenarioInfo Unspecified { get; } = new()
    {
        Location = "<unspecified>",
        ConnectionMethod = ConnectionMethod.Unknown,
        AccessMethod = NetworkAccessMethod.Unknown,
        ProblemSymptom = ProblemSymptom.Unknown,
        ImpactScope = ImpactScope.Unknown
    };

    public required string Location { get; init; }

    public required ConnectionMethod ConnectionMethod { get; init; }

    public required NetworkAccessMethod AccessMethod { get; init; }

    public required ProblemSymptom ProblemSymptom { get; init; }

    public required ImpactScope ImpactScope { get; init; }
}
