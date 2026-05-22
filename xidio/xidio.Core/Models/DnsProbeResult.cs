using System.Net;

namespace xidio.Core.Models;

public sealed class DnsProbeResult
{
    public required string QueryName { get; init; }

    public required string QueryType { get; init; }

    public string? DnsServer { get; init; }

    public required bool Succeeded { get; init; }

    public required IReadOnlyList<IPAddress> Addresses { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Error { get; init; }
}
