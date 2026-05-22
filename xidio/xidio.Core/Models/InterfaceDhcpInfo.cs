namespace xidio.Core.Models;

public sealed class InterfaceDhcpInfo
{
    public bool? IsEnabled { get; init; }

    public DateTimeOffset? LeaseObtained { get; init; }

    public DateTimeOffset? LeaseExpires { get; init; }
}
