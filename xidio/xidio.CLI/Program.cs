using xidio.Core.Diagnostics;
using xidio.Platform.Windows;

namespace xidio.CLI;

internal static class Program
{
    private static void Main(string[] args)
    {
        var collector = new NetworkDiagnosticsCollector(new WindowsPlatformNetworkDiagnosticsProvider());
        var report = collector.Collect();

        ConsoleDiagnosticReporter.Print(report);
    }
}
