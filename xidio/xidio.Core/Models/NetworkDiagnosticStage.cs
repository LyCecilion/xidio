namespace xidio.Core.Models;

public enum NetworkDiagnosticStage
{
    Starting,
    SystemInfo,
    Clock,
    NetworkInterfaces,
    PhysicalAdapters,
    DriverInfo,
    PrimaryInterfaces,
    WirelessInfo,
    NetworkDetails,
    DefaultRoutes,
    SystemProxy,
    ActiveProbes,
    Completed
}
