namespace xidio.Core.Models;

public enum NetworkDiagnosticStage
{
    Starting,
    NetworkInterfaces,
    PhysicalAdapters,
    DriverInfo,
    PrimaryInterfaces,
    WirelessInfo,
    NetworkDetails,
    SystemProxy,
    Completed
}
