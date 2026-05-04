namespace xidio.Core.Models;

public sealed class SystemProxyInfo
{
    public required bool IsEnabled { get; init; }

    public Uri? ProxyUri { get; init; }
}
