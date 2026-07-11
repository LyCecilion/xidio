namespace xidio.Core.Models;

public sealed class SystemProxyInfo
{
    public bool WasCollected { get; init; }

    public required bool IsEnabled { get; init; }

    public Uri? ProxyUri { get; init; }
}
