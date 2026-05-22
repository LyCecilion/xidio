namespace xidio.Core.Models;

public sealed class VisibleWirelessNetworkInfo
{
    public required string Ssid { get; init; }

    public int? SignalQualityPercent { get; init; }

    public string? Authentication { get; init; }

    public string? Cipher { get; init; }

    public int? BssidCount { get; init; }

    public bool? IsConnectable { get; init; }
}
