using Spectre.Console;
using xidio.Core.Abstractions;
using xidio.Core.Diagnostics;
#if WINDOWS
using xidio.Platform.Windows;
#else
using xidio.Platform.macOS;
#endif

namespace xidio.CLI;

internal static class Program
{
    private static void Main(string[] args)
    {
        var collector = new NetworkDiagnosticsCollector(CreatePlatformProvider());
        var report = collector.Collect();
        
        ConsoleDiagnosticReporter.Print(report);
    }

    private static IPlatformNetworkDiagnosticsProvider CreatePlatformProvider()
    {
#if WINDOWS
        return new WindowsPlatformNetworkDiagnosticsProvider();
#else
        if (OperatingSystem.IsMacOS())
            return new MacPlatformNetworkDiagnosticsProvider();

        return NoopPlatformNetworkDiagnosticsProvider.Instance;
#endif
    }
}
