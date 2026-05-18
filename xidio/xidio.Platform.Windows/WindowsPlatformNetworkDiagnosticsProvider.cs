using System.Management;
using System.Net.NetworkInformation;
using xidio.Core.Abstractions;
using xidio.Core.Models;

namespace xidio.Platform.Windows;

public sealed class WindowsPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    public async ValueTask<IReadOnlyCollection<string>> GetPhysicalNetworkInterfaceIdsAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetPhysicalNetworkInterfaceIdsCore();
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<NetworkAdapterDriverInfo>> GetNetworkAdapterDriverInfosAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetNetworkAdapterDriverInfosCore();
        }, cancellationToken);
    }

    public async ValueTask<WirelessConnectionInfo?> GetWirelessConnectionInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(networkInterface.Id, out var interfaceGuid))
            return null;

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return WindowsWlanApi.GetCurrentConnection(interfaceGuid);
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<InterfaceMetricInfo>> GetInterfaceMetricsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetInterfaceMetricsCore(networkInterface, cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<InterfaceRouteInfo>> GetRoutesAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetRoutesCore(networkInterface, cancellationToken);
        }, cancellationToken);
    }

    private static IReadOnlyCollection<string> GetPhysicalNetworkInterfaceIdsCore()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var searcher = new ManagementObjectSearcher(
            "SELECT GUID, Name, Description, NetConnectionID, PhysicalAdapter, " +
            "PNPDeviceID, Manufacturer, ServiceName " +
            "FROM Win32_NetworkAdapter " +
            "WHERE PhysicalAdapter = TRUE AND GUID IS NOT NULL");

        foreach (ManagementObject obj in searcher.Get())
        {
            var guid = obj["GUID"]?.ToString();

            if (string.IsNullOrWhiteSpace(guid))
                continue;

            var name = obj["Name"]?.ToString() ?? "";
            var description = obj["Description"]?.ToString() ?? "";
            var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
            var serviceName = obj["ServiceName"]?.ToString() ?? "";
            var netConnectionId = obj["NetConnectionID"]?.ToString() ?? "";

            if (LooksLikeVirtualAdapter(name, description, manufacturer, pnpDeviceId, serviceName, netConnectionId))
                continue;

            result.Add(NormalizeGuid(guid));
        }

        return result;
    }

    private static IReadOnlyList<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfosCore()
    {
        var driverVersions = GetDriverVersionsByPnpDeviceId();
        var result = new List<NetworkAdapterDriverInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Description, NetConnectionID, PhysicalAdapter, " +
            "PNPDeviceID, Manufacturer, ServiceName " +
            "FROM Win32_NetworkAdapter " +
            "WHERE PhysicalAdapter = TRUE AND PNPDeviceID IS NOT NULL");

        foreach (ManagementObject obj in searcher.Get())
        {
            var name = obj["Name"]?.ToString() ?? "";
            var description = obj["Description"]?.ToString() ?? "";
            var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
            var serviceName = obj["ServiceName"]?.ToString() ?? "";
            var netConnectionId = obj["NetConnectionID"]?.ToString() ?? "";

            if (LooksLikeVirtualAdapter(name, description, manufacturer, pnpDeviceId, serviceName, netConnectionId))
                continue;

            driverVersions.TryGetValue(pnpDeviceId, out var driverVersion);

            result.Add(new NetworkAdapterDriverInfo
            {
                Name = string.IsNullOrWhiteSpace(netConnectionId) ? name : netConnectionId,
                Description = description,
                DriverVersion = string.IsNullOrWhiteSpace(driverVersion) ? "<unknown>" : driverVersion
            });
        }

        return result;
    }

    private static IReadOnlyList<InterfaceMetricInfo> GetInterfaceMetricsCore(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var result = new List<InterfaceMetricInfo>();

        foreach (var interfaceIndex in GetInterfaceIndices(networkInterface))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\ROOT\StandardCimv2"),
                new ObjectQuery(
                    "SELECT AddressFamily, InterfaceMetric " +
                    "FROM MSFT_NetIPInterface " +
                    $"WHERE InterfaceIndex = {interfaceIndex}"));

            foreach (ManagementObject obj in searcher.Get())
            {
                var addressFamily = GetWmiInt32(obj, "AddressFamily");
                var interfaceMetric = GetWmiInt32(obj, "InterfaceMetric");

                if (addressFamily == 0)
                    continue;

                result.Add(new InterfaceMetricInfo
                {
                    AddressFamily = addressFamily,
                    InterfaceMetric = interfaceMetric
                });
            }
        }

        return result;
    }

    private static IReadOnlyList<InterfaceRouteInfo> GetRoutesCore(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var result = new List<InterfaceRouteInfo>();

        foreach (var interfaceIndex in GetInterfaceIndices(networkInterface))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\ROOT\StandardCimv2"),
                new ObjectQuery(
                    "SELECT DestinationPrefix, NextHop, AddressFamily, RouteMetric, InterfaceMetric " +
                    "FROM MSFT_NetRoute " +
                    $"WHERE InterfaceIndex = {interfaceIndex}"));

            foreach (ManagementObject obj in searcher.Get())
            {
                var destinationPrefix = obj["DestinationPrefix"]?.ToString();

                if (string.IsNullOrWhiteSpace(destinationPrefix))
                    continue;

                result.Add(new InterfaceRouteInfo
                {
                    DestinationPrefix = destinationPrefix,
                    NextHop = obj["NextHop"]?.ToString() ?? "",
                    AddressFamily = GetWmiInt32(obj, "AddressFamily"),
                    RouteMetric = GetWmiInt32(obj, "RouteMetric"),
                    InterfaceMetric = GetWmiInt32(obj, "InterfaceMetric")
                });
            }
        }

        return result;
    }

    private static Dictionary<string, string> GetDriverVersionsByPnpDeviceId()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver " +
            "WHERE DeviceClass = 'NET'");

        foreach (ManagementObject obj in searcher.Get())
        {
            var deviceId = obj["DeviceID"]?.ToString();
            var driverVersion = obj["DriverVersion"]?.ToString();

            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(driverVersion))
                continue;

            result[deviceId] = driverVersion;
        }

        return result;
    }

    private static List<int> GetInterfaceIndices(NetworkInterface networkInterface)
    {
        var result = new List<int>();
        IPInterfaceProperties properties;

        try
        {
            properties = networkInterface.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        try
        {
            var ipv4Index = properties.GetIPv4Properties()?.Index;

            if (ipv4Index is > 0)
                result.Add(ipv4Index.Value);
        }
        catch (NetworkInformationException)
        {
        }

        try
        {
            var ipv6Index = properties.GetIPv6Properties()?.Index;

            if (ipv6Index is > 0 && !result.Contains(ipv6Index.Value))
                result.Add(ipv6Index.Value);
        }
        catch (NetworkInformationException)
        {
        }

        return result;
    }

    private static int GetWmiInt32(ManagementBaseObject obj, string propertyName)
    {
        return int.TryParse(obj[propertyName]?.ToString(), out var value)
            ? value
            : 0;
    }

    private static string NormalizeGuid(string guid)
    {
        return guid.Trim().Trim('{', '}').ToUpperInvariant();
    }

    private static bool LooksLikeVirtualAdapter(
        string name,
        string description,
        string manufacturer = "",
        string pnpDeviceId = "",
        string serviceName = "",
        string netConnectionId = "")
    {
        var text = string.Join(" ", new[]
        {
            name,
            description,
            manufacturer,
            pnpDeviceId,
            serviceName,
            netConnectionId
        }).ToLowerInvariant();

        return
            text.Contains("vmware") ||
            text.Contains("vmnet") ||
            text.Contains("hyper-v") ||
            text.Contains("hyperv") ||
            text.Contains("vswitch") ||
            text.Contains("vethernet") ||
            text.Contains("virtualbox") ||
            text.Contains("host-only") ||
            text.Contains("wsl") ||
            text.Contains("tap-windows") ||
            text.Contains("tap adapter") ||
            text.Contains("tun adapter") ||
            text.Contains("wireguard") ||
            text.Contains("tailscale") ||
            text.Contains("zerotier") ||
            text.Contains("npcap") ||
            text.Contains("winpcap") ||
            text.Contains("packet driver") ||
            text.Contains("wi-fi direct") ||
            text.Contains("wifi direct") ||
            text.Contains("virtual wifi") ||
            text.Contains("bluetooth") ||
            text.Contains("蓝牙");
    }
}
