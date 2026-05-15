using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Platform.macOS;

public sealed class MacPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    public IReadOnlyCollection<string> GetPhysicalNetworkInterfaceIds()
    {
        var output = RunCommand("/usr/sbin/networksetup", "-listallhardwareports");
        if (output is null)
            return Array.Empty<string>();

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                continue;

            var device = trimmed["Device:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(device))
                ids.Add(device);
        }

        return ids;
    }

    public IReadOnlyList<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfos()
    {
        var hardwarePorts = ParseHardwarePorts();
        if (hardwarePorts.Count == 0)
            return Array.Empty<NetworkAdapterDriverInfo>();

        var networkData = RunCommand("/usr/sbin/system_profiler", "SPNetworkDataType");
        var driverVersions = networkData is not null
            ? ParseNetworkDriverVersions(networkData)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<NetworkAdapterDriverInfo>();
        foreach (var (device, portName) in hardwarePorts)
        {
            driverVersions.TryGetValue(device, out var driverVersion);

            result.Add(new NetworkAdapterDriverInfo
            {
                Name = portName,
                Description = device,
                DriverVersion = string.IsNullOrWhiteSpace(driverVersion) ? "<unknown>" : driverVersion
            });
        }

        return result;
    }

    public WirelessConnectionInfo? GetWirelessConnectionInfo(NetworkInterface networkInterface)
    {
        try
        {
            var output = RunCommand(
                "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport",
                "-I");

            if (output is null)
                return null;

            string? ssid = null;
            string? bssid = null;

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SSID:", StringComparison.OrdinalIgnoreCase))
                {
                    ssid = trimmed["SSID:".Length..].Trim();
                }
                else if (trimmed.StartsWith("BSSID:", StringComparison.OrdinalIgnoreCase))
                {
                    bssid = trimmed["BSSID:".Length..].Trim();
                }

                if (ssid is not null && bssid is not null)
                    break;
            }

            if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(bssid))
                return null;

            return new WirelessConnectionInfo
            {
                Ssid = ssid,
                Bssid = bssid
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<InterfaceMetricInfo> GetInterfaceMetrics(NetworkInterface networkInterface)
    {
        var result = new List<InterfaceMetricInfo>();
        var interfaceId = networkInterface.Id;

        var v4Metrics = GetInterfaceMetricFromRoutes(interfaceId, "inet");
        if (v4Metrics.HasValue)
        {
            result.Add(new InterfaceMetricInfo
            {
                AddressFamily = (int)AddressFamily.InterNetwork,
                InterfaceMetric = v4Metrics.Value
            });
        }

        var v6Metrics = GetInterfaceMetricFromRoutes(interfaceId, "inet6");
        if (v6Metrics.HasValue)
        {
            result.Add(new InterfaceMetricInfo
            {
                AddressFamily = (int)AddressFamily.InterNetworkV6,
                InterfaceMetric = v6Metrics.Value
            });
        }

        return result;
    }

    public IReadOnlyList<InterfaceRouteInfo> GetRoutes(NetworkInterface networkInterface)
    {
        var result = new List<InterfaceRouteInfo>();
        var interfaceId = networkInterface.Id;

        result.AddRange(ParseNetstatRoutes(interfaceId, "inet", (int)AddressFamily.InterNetwork));
        result.AddRange(ParseNetstatRoutes(interfaceId, "inet6", (int)AddressFamily.InterNetworkV6));

        return result;
    }

    private static List<InterfaceRouteInfo> ParseNetstatRoutes(
        string interfaceId,
        string addressFamilyFlag,
        int addressFamily)
    {
        var routes = new List<InterfaceRouteInfo>();
        var output = RunCommand("/usr/sbin/netstat", $"-rn -f {addressFamilyFlag}");
        if (output is null)
            return routes;

        var lines = output.Split('\n');
        var inTable = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (inTable)
                    break;
                continue;
            }

            if (line.StartsWith("Internet", StringComparison.OrdinalIgnoreCase))
            {
                inTable = true;
                continue;
            }

            if (!inTable || line.StartsWith("Destination", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
                continue;

            var destination = parts[0];
            var gateway = parts[1];
            var flags = parts[2];
            var netif = parts[3];

            if (!string.Equals(netif, interfaceId, StringComparison.OrdinalIgnoreCase) &&
                !netif.StartsWith(interfaceId, StringComparison.OrdinalIgnoreCase))
                continue;

            routes.Add(new InterfaceRouteInfo
            {
                DestinationPrefix = destination,
                NextHop = gateway.StartsWith("link#", StringComparison.OrdinalIgnoreCase) ? "" : gateway,
                AddressFamily = addressFamily,
                RouteMetric = 0,
                InterfaceMetric = 0
            });
        }

        return routes;
    }

    private static int? GetInterfaceMetricFromRoutes(string interfaceId, string addressFamilyFlag)
    {
        var output = RunCommand("/usr/sbin/netstat", $"-rn -f {addressFamilyFlag}");
        if (output is null)
            return null;

        var lines = output.Split('\n');
        var inTable = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (inTable)
                    break;
                continue;
            }

            if (line.StartsWith("Internet", StringComparison.OrdinalIgnoreCase))
            {
                inTable = true;
                continue;
            }

            if (!inTable || line.StartsWith("Destination", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
                continue;

            var netif = parts[3];
            if (string.Equals(netif, interfaceId, StringComparison.OrdinalIgnoreCase) ||
                netif.StartsWith(interfaceId, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseHardwarePorts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var output = RunCommand("/usr/sbin/networksetup", "-listallhardwareports");
        if (output is null)
            return result;

        string? currentPort = null;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Hardware Port:", StringComparison.OrdinalIgnoreCase))
            {
                currentPort = trimmed["Hardware Port:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase) && currentPort is not null)
            {
                var device = trimmed["Device:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(device))
                    result[device] = currentPort;
                currentPort = null;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ParseNetworkDriverVersions(string systemProfilerOutput)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = systemProfilerOutput.Split('\n');

        string? currentDevice = null;
        string? currentKext = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("BSD Device Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentDevice = trimmed["BSD Device Name:".Length..].Trim();
            }
            else if (trimmed.StartsWith("Kext Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentKext = trimmed["Kext Name:".Length..].Trim();
            }

            if (currentDevice is not null && currentKext is not null)
            {
                result[currentDevice] = currentKext;
                currentDevice = null;
                currentKext = null;
            }
        }

        return result;
    }

    private static string? RunCommand(string command, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
