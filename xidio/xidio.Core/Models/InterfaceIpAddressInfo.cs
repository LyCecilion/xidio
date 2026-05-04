using System.Net;

namespace xidio.Core.Models;

public sealed class InterfaceIpAddressInfo
{
    public required IPAddress Address { get; init; }

    public required int PrefixLength { get; init; }

    public IPAddress? IPv4Mask { get; init; }
}
