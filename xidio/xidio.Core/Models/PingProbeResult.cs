namespace xidio.Core.Models;

public sealed class PingProbeResult
{
    public required bool Succeeded { get; init; }

    public long? RoundtripTimeMilliseconds { get; init; }

    public string? Status { get; init; }

    public string? Error { get; init; }
}
