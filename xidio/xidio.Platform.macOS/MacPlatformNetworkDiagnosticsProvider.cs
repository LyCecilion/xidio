using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Platform.macOS;

public sealed class MacPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    public async ValueTask<IReadOnlyCollection<string>> GetPhysicalNetworkInterfaceIdsAsync(CancellationToken cancellationToken)
    {
        var output = await RunCommandAsync("/usr/sbin/networksetup", "-listallhardwareports", cancellationToken);
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

    public async ValueTask<IReadOnlyList<NetworkAdapterDriverInfo>> GetNetworkAdapterDriverInfosAsync(CancellationToken cancellationToken)
    {
        var hardwarePorts = await ParseHardwarePortsAsync(cancellationToken);
        if (hardwarePorts.Count == 0)
            return Array.Empty<NetworkAdapterDriverInfo>();

        var networkData = await RunCommandAsync("/usr/sbin/system_profiler", "SPNetworkDataType", cancellationToken);
        var driverVersions = networkData is not null
            ? ParseNetworkDriverVersions(networkData)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<NetworkAdapterDriverInfo>();
        foreach (var (device, portName) in hardwarePorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    public async ValueTask<WirelessConnectionInfo?> GetWirelessConnectionInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunCommandAsync(
                "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport",
                "-I",
                cancellationToken);

            if (output is null)
                return null;

            string? ssid = null;
            string? bssid = null;

            foreach (var line in output.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<IReadOnlyList<InterfaceMetricInfo>> GetInterfaceMetricsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var result = new List<InterfaceMetricInfo>();
        var interfaceId = networkInterface.Id;

        var v4Metrics = await GetInterfaceMetricFromRoutesAsync(interfaceId, "inet", cancellationToken);
        if (v4Metrics.HasValue)
        {
            result.Add(new InterfaceMetricInfo
            {
                AddressFamily = (int)AddressFamily.InterNetwork,
                InterfaceMetric = v4Metrics.Value
            });
        }

        var v6Metrics = await GetInterfaceMetricFromRoutesAsync(interfaceId, "inet6", cancellationToken);
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

    public async ValueTask<IReadOnlyList<InterfaceRouteInfo>> GetRoutesAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var result = new List<InterfaceRouteInfo>();
        var interfaceId = networkInterface.Id;

        result.AddRange(await ParseNetstatRoutesAsync(interfaceId, "inet", (int)AddressFamily.InterNetwork, cancellationToken));
        result.AddRange(await ParseNetstatRoutesAsync(interfaceId, "inet6", (int)AddressFamily.InterNetworkV6, cancellationToken));

        return result;
    }

    private static async Task<List<InterfaceRouteInfo>> ParseNetstatRoutesAsync(
        string interfaceId,
        string addressFamilyFlag,
        int addressFamily,
        CancellationToken cancellationToken)
    {
        var routes = new List<InterfaceRouteInfo>();
        var output = await RunCommandAsync("/usr/sbin/netstat", $"-rn -f {addressFamilyFlag}", cancellationToken);
        if (output is null)
            return routes;

        var lines = output.Split('\n');
        var inTable = false;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

    private static async Task<int?> GetInterfaceMetricFromRoutesAsync(
        string interfaceId,
        string addressFamilyFlag,
        CancellationToken cancellationToken)
    {
        var output = await RunCommandAsync("/usr/sbin/netstat", $"-rn -f {addressFamilyFlag}", cancellationToken);
        if (output is null)
            return null;

        var lines = output.Split('\n');
        var inTable = false;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

    private static async Task<Dictionary<string, string>> ParseHardwarePortsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var output = await RunCommandAsync("/usr/sbin/networksetup", "-listallhardwareports", cancellationToken);
        if (output is null)
            return result;

        string? currentPort = null;
        foreach (var line in output.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

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

    private static async Task<string?> RunCommandAsync(
        string command,
        string arguments,
        CancellationToken cancellationToken)
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

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            await errorTask;

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw;
        }
        catch
        {
            return null;
        }
    }
}
