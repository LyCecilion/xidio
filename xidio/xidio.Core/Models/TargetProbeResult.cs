namespace xidio.Core.Models;

public sealed class TargetProbeResult
{
    public required ProbeTargetInfo Target { get; init; }

    public required IReadOnlyList<DnsProbeResult> SystemDnsResults { get; init; }

    public required IReadOnlyList<DnsProbeResult> DirectDnsResults { get; init; }

    public required PingProbeResult Ping { get; init; }

    public required IReadOnlyList<TcpConnectProbeResult> TcpConnectResults { get; init; }

    public HttpProbeResult? Http { get; init; }

    public HttpProbeResult? Https { get; init; }
}
