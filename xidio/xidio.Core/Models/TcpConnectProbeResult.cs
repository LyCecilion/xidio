namespace xidio.Core.Models;

public sealed class TcpConnectProbeResult
{
    public required int Port { get; init; }

    public required bool Succeeded { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Error { get; init; }
}
