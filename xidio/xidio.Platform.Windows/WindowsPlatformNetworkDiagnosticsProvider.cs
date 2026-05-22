using System.Management;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using xidio.Core.Abstractions;
using xidio.Core.Diagnostics;
using xidio.Core.Models;

namespace xidio.Platform.Windows;

public sealed class WindowsPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    public ValueTask<OperatingSystemDiagnosticInfo?> GetOperatingSystemInfoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<OperatingSystemDiagnosticInfo?>(new OperatingSystemDiagnosticInfo
        {
            Family = "Windows",
            Description = RuntimeInformation.OSDescription,
            Version = Environment.OSVersion.VersionString,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            XidioVersion = XidioVersion.InformationalVersion,
            IsElevated = IsWindowsAdministrator()
        });
    }

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

    public async ValueTask<InterfacePlatformInfo?> GetInterfacePlatformInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetInterfacePlatformInfoCore(networkInterface);
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

    public async ValueTask<InterfaceDhcpInfo?> GetInterfaceDhcpInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetInterfaceDhcpInfoCore(networkInterface);
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

    public async ValueTask<IReadOnlyList<NetworkNeighborInfo>> GetNetworkNeighborsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetNetworkNeighborsCore(networkInterface, cancellationToken);
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<DefaultRouteInfo>> GetDefaultRoutesAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetDefaultRoutesCore(cancellationToken);
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

    private static HashSet<string> GetPhysicalNetworkInterfaceIdsCore()
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

    private static List<NetworkAdapterDriverInfo> GetNetworkAdapterDriverInfosCore()
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

    private static InterfacePlatformInfo? GetInterfacePlatformInfoCore(NetworkInterface networkInterface)
    {
        var isEnabled = GetNetEnabled(networkInterface.Id);
        var mtu = GetInterfaceMtu(networkInterface);

        return isEnabled is null && mtu is null
            ? null
            : new InterfacePlatformInfo
            {
                IsEnabled = isEnabled,
                Mtu = mtu
            };
    }

    private static InterfaceDhcpInfo? GetInterfaceDhcpInfoCore(NetworkInterface networkInterface)
    {
        foreach (var interfaceIndex in GetInterfaceIndices(networkInterface))
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DHCPEnabled, DHCPLeaseObtained, DHCPLeaseExpires " +
                "FROM Win32_NetworkAdapterConfiguration " +
                $"WHERE InterfaceIndex = {interfaceIndex}");

            foreach (ManagementObject obj in searcher.Get())
            {
                return new InterfaceDhcpInfo
                {
                    IsEnabled = GetWmiBool(obj, "DHCPEnabled"),
                    LeaseObtained = GetWmiDateTimeOffset(obj, "DHCPLeaseObtained"),
                    LeaseExpires = GetWmiDateTimeOffset(obj, "DHCPLeaseExpires")
                };
            }
        }

        return null;
    }

    private static List<InterfaceMetricInfo> GetInterfaceMetricsCore(
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

    private static List<NetworkNeighborInfo> GetNetworkNeighborsCore(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var result = new List<NetworkNeighborInfo>();

        foreach (var interfaceIndex in GetInterfaceIndices(networkInterface))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\ROOT\StandardCimv2"),
                new ObjectQuery(
                    "SELECT IPAddress, LinkLayerAddress, AddressFamily, State " +
                    "FROM MSFT_NetNeighbor " +
                    $"WHERE InterfaceIndex = {interfaceIndex}"));

            foreach (ManagementObject obj in searcher.Get())
            {
                var ipAddress = obj["IPAddress"]?.ToString();

                if (string.IsNullOrWhiteSpace(ipAddress))
                    continue;

                result.Add(new NetworkNeighborInfo
                {
                    IpAddress = ipAddress,
                    LinkLayerAddress = obj["LinkLayerAddress"]?.ToString() ?? "",
                    AddressFamily = GetWmiInt32(obj, "AddressFamily"),
                    State = FormatNeighborState(GetWmiInt32(obj, "State"))
                });
            }
        }

        return result;
    }

    private static List<DefaultRouteInfo> GetDefaultRoutesCore(CancellationToken cancellationToken)
    {
        var result = new List<DefaultRouteInfo>();

        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"\\.\ROOT\StandardCimv2"),
            new ObjectQuery(
                "SELECT DestinationPrefix, NextHop, AddressFamily, InterfaceIndex, InterfaceAlias, RouteMetric, InterfaceMetric " +
                "FROM MSFT_NetRoute " +
                "WHERE DestinationPrefix = '0.0.0.0/0' OR DestinationPrefix = '::/0'"));

        foreach (ManagementObject obj in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPrefix = obj["DestinationPrefix"]?.ToString();

            if (string.IsNullOrWhiteSpace(destinationPrefix))
                continue;

            result.Add(new DefaultRouteInfo
            {
                DestinationPrefix = destinationPrefix,
                NextHop = obj["NextHop"]?.ToString() ?? "",
                AddressFamily = GetWmiInt32(obj, "AddressFamily"),
                InterfaceIndex = GetWmiInt32(obj, "InterfaceIndex"),
                InterfaceAlias = obj["InterfaceAlias"]?.ToString() ?? "",
                RouteMetric = GetWmiInt32(obj, "RouteMetric"),
                InterfaceMetric = GetWmiInt32(obj, "InterfaceMetric")
            });
        }

        return result
            .OrderBy(route => route.AddressFamily)
            .ThenBy(route => route.TotalMetric)
            .ThenBy(route => route.InterfaceAlias)
            .ToList();
    }

    private static List<InterfaceRouteInfo> GetRoutesCore(
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

    private static bool? GetWmiBool(ManagementBaseObject obj, string propertyName)
    {
        return bool.TryParse(obj[propertyName]?.ToString(), out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? GetWmiDateTimeOffset(ManagementBaseObject obj, string propertyName)
    {
        var value = obj[propertyName]?.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(value));
        }
        catch
        {
            return null;
        }
    }

    private static bool? GetNetEnabled(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var interfaceGuid))
            return null;

        using var searcher = new ManagementObjectSearcher(
            "SELECT NetEnabled FROM Win32_NetworkAdapter " +
            $"WHERE GUID = '{{{interfaceGuid}}}'");

        foreach (ManagementObject obj in searcher.Get())
            return GetWmiBool(obj, "NetEnabled");

        return null;
    }

    private static int? GetInterfaceMtu(NetworkInterface networkInterface)
    {
        try
        {
            var properties = networkInterface.GetIPProperties();
            var ipv4Mtu = properties.GetIPv4Properties()?.Mtu;

            if (ipv4Mtu is > 0)
                return ipv4Mtu;

            var ipv6Mtu = properties.GetIPv6Properties()?.Mtu;
            return ipv6Mtu is > 0 ? ipv6Mtu : null;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string FormatNeighborState(int state)
    {
        return state switch
        {
            0 => "Unreachable",
            1 => "Incomplete",
            2 => "Probe",
            3 => "Delay",
            4 => "Stale",
            5 => "Reachable",
            6 => "Permanent",
            _ => state.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
