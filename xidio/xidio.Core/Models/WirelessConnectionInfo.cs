namespace xidio.Core.Models;

public sealed class WirelessConnectionInfo
{
    public required string Ssid { get; init; }

    public required string Bssid { get; init; }

    public string ApMac => Bssid;
}
