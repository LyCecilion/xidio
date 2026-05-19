namespace xidio.Core.Models;

public sealed class WirelessConnectionInfo
{
    public required string Ssid { get; init; }

    public required string Bssid { get; init; }

    public int? SignalQualityPercent { get; init; }

    public int? RssiDbm { get; init; }

    public int? Channel { get; init; }

    public double? FrequencyGhz { get; init; }

    public string? PhyType { get; init; }

    public string? Authentication { get; init; }

    public string? Cipher { get; init; }

    public double? ReceiveRateMbps { get; init; }

    public double? TransmitRateMbps { get; init; }

    public TimeSpan? ConnectionDuration { get; init; }

    public IReadOnlyList<VisibleWirelessNetworkInfo> VisibleNetworks { get; init; } = [];

    public string ApMac => Bssid;
}
