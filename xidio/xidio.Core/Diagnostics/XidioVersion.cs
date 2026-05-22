using System.Reflection;

namespace xidio.Core.Diagnostics;

public static class XidioVersion
{
    public static string InformationalVersion { get; } = GetInformationalVersion();

    private static string GetInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(XidioVersion).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion;

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
