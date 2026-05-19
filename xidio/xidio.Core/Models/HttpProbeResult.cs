namespace xidio.Core.Models;

public sealed class HttpProbeResult
{
    public required Uri Uri { get; init; }

    public required bool Succeeded { get; init; }

    public int? StatusCode { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Error { get; init; }
}
