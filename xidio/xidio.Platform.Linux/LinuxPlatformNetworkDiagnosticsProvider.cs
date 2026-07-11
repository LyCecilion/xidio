using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using xidio.Core.Abstractions;
using xidio.Core.Diagnostics;
using xidio.Core.Models;

namespace xidio.Platform.Linux;

public sealed class LinuxPlatformNetworkDiagnosticsProvider : IPlatformNetworkDiagnosticsProvider
{
    private const string SysClassNet = "/sys/class/net";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    public async ValueTask<OperatingSystemDiagnosticInfo?> GetOperatingSystemInfoAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var osRelease = await ReadOsReleaseAsync(cancellationToken);
        osRelease.TryGetValue("PRETTY_NAME", out var description);
        osRelease.TryGetValue("VERSION_ID", out var version);

        return new OperatingSystemDiagnosticInfo
        {
            Family = "Linux",
            Description = string.IsNullOrWhiteSpace(description)
                ? RuntimeInformation.OSDescription
                : description,
            Version = string.IsNullOrWhiteSpace(version)
                ? Environment.OSVersion.VersionString
                : version,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            XidioVersion = XidioVersion.InformationalVersion,
            IsElevated = GetEffectiveUserId() == 0
        };
    }

    public ValueTask<IReadOnlyCollection<string>> GetPhysicalNetworkInterfaceIdsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(SysClassNet))
            return ValueTask.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        var interfaces = Directory.EnumerateDirectories(SysClassNet)
            .Where(path => Directory.Exists(Path.Combine(path, "device")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyCollection<string>>(interfaces);
    }

    public async ValueTask<IReadOnlyList<NetworkAdapterDriverInfo>> GetNetworkAdapterDriverInfosAsync(
        CancellationToken cancellationToken)
    {
        var physicalIds = await GetPhysicalNetworkInterfaceIdsAsync(cancellationToken);
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .ToDictionary(networkInterface => networkInterface.Name, StringComparer.Ordinal);
        var result = new List<NetworkAdapterDriverInfo>();

        foreach (var interfaceId in physicalIds.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            networkInterfaces.TryGetValue(interfaceId, out var networkInterface);

            var driverName = GetDriverName(interfaceId);
            var driverVersion = driverName is null
                ? null
                : await ReadTrimmedTextAsync($"/sys/module/{driverName}/version", cancellationToken);

            result.Add(new NetworkAdapterDriverInfo
            {
                Name = interfaceId,
                Description = driverName is null
                    ? networkInterface?.Description ?? interfaceId
                    : $"{networkInterface?.Description ?? interfaceId} ({driverName})",
                DriverVersion = string.IsNullOrWhiteSpace(driverVersion)
                    ? "<unknown>"
                    : driverVersion
            });
        }

        return result;
    }

    public async ValueTask<InterfacePlatformInfo?> GetInterfacePlatformInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var interfacePath = Path.Combine(SysClassNet, networkInterface.Name);
        if (!Directory.Exists(interfacePath))
            return null;

        var flagsText = await ReadTrimmedTextAsync(Path.Combine(interfacePath, "flags"), cancellationToken);
        var mtuText = await ReadTrimmedTextAsync(Path.Combine(interfacePath, "mtu"), cancellationToken);

        bool? isEnabled = null;
        if (TryParseHexUInt64(flagsText, out var flags))
            isEnabled = (flags & 0x1) != 0;

        int? mtu = int.TryParse(mtuText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMtu)
            ? parsedMtu
            : null;

        return new InterfacePlatformInfo
        {
            IsEnabled = isEnabled,
            Mtu = mtu
        };
    }

    public async ValueTask<WirelessConnectionInfo?> GetWirelessConnectionInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var iwOutput = await RunCommandAsync(
            "iw",
            ["dev", networkInterface.Name, "link"],
            cancellationToken);
        var current = ParseIwLink(iwOutput?.StandardOutput);

        var nmcliOutput = await RunCommandAsync(
            "nmcli",
            [
                "-t",
                "--escape", "yes",
                "-f", "IN-USE,SSID,BSSID,SIGNAL,FREQ,CHAN,RATE,SECURITY",
                "device", "wifi", "list",
                "ifname", networkInterface.Name,
                "--rescan", "no"
            ],
            cancellationToken);
        var wifiRows = ParseNmcliWifiRows(nmcliOutput?.StandardOutput);
        var activeRow = wifiRows.FirstOrDefault(row => row.IsActive);

        if (current is null && activeRow is null)
            return null;

        var ssid = current?.Ssid ?? activeRow?.Ssid;
        var bssid = current?.Bssid ?? activeRow?.Bssid;
        if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(bssid))
            return null;

        return new WirelessConnectionInfo
        {
            Ssid = ssid,
            Bssid = bssid,
            SignalQualityPercent = activeRow?.SignalQualityPercent,
            RssiDbm = current?.RssiDbm,
            Channel = current?.Channel ?? activeRow?.Channel,
            FrequencyGhz = current?.FrequencyGhz ?? activeRow?.FrequencyGhz,
            PhyType = InferPhyType(current?.RateDescription),
            Authentication = activeRow?.Security,
            ReceiveRateMbps = current?.ReceiveRateMbps ?? activeRow?.RateMbps,
            TransmitRateMbps = current?.TransmitRateMbps ?? activeRow?.RateMbps,
            VisibleNetworks = CreateVisibleNetworks(wifiRows)
        };
    }

    public async ValueTask<InterfaceDhcpInfo?> GetInterfaceDhcpInfoAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var output = await RunCommandAsync(
            "nmcli",
            ["-t", "-f", "DHCP4.OPTION,DHCP6.OPTION", "device", "show", networkInterface.Name],
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output?.StandardOutput))
            return null;

        long? expiry = null;
        int? leaseSeconds = null;

        foreach (var line in output.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator < 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (name.EndsWith("expiry", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedExpiry))
            {
                expiry = parsedExpiry;
            }
            else if (name.EndsWith("dhcp_lease_time", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLease))
            {
                leaseSeconds = parsedLease;
            }
        }

        var leaseExpires = expiry is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(expiry.Value)
            : (DateTimeOffset?)null;

        return new InterfaceDhcpInfo
        {
            IsEnabled = true,
            LeaseExpires = leaseExpires,
            LeaseObtained = leaseExpires is not null && leaseSeconds is > 0
                ? leaseExpires.Value - TimeSpan.FromSeconds(leaseSeconds.Value)
                : null
        };
    }

    public async ValueTask<IReadOnlyList<InterfaceMetricInfo>> GetInterfaceMetricsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var routes = await GetRoutesAsync(networkInterface, cancellationToken);

        return routes
            .GroupBy(route => route.AddressFamily)
            .Select(group => new InterfaceMetricInfo
            {
                AddressFamily = group.Key,
                InterfaceMetric = group
                    .Where(route => route.DestinationPrefix is "0.0.0.0/0" or "::/0")
                    .Select(route => route.TotalMetric)
                    .DefaultIfEmpty(group
                        .Where(route => route.TotalMetric > 0)
                        .Select(route => route.TotalMetric)
                        .DefaultIfEmpty(0)
                        .Min())
                    .Min()
            })
            .OrderBy(metric => metric.AddressFamily)
            .ToList();
    }

    public async ValueTask<IReadOnlyList<InterfaceRouteInfo>> GetRoutesAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var routes = new List<InterfaceRouteInfo>();
        routes.AddRange(await ReadRoutesAsync(AddressFamily.InterNetwork, networkInterface.Name, cancellationToken));
        routes.AddRange(await ReadRoutesAsync(AddressFamily.InterNetworkV6, networkInterface.Name, cancellationToken));
        return routes;
    }

    public async ValueTask<IReadOnlyList<NetworkNeighborInfo>> GetNetworkNeighborsAsync(
        NetworkInterface networkInterface,
        CancellationToken cancellationToken)
    {
        var output = await RunCommandAsync("ip", ["-j", "neigh", "show"], cancellationToken);
        if (string.IsNullOrWhiteSpace(output?.StandardOutput))
            return Array.Empty<NetworkNeighborInfo>();

        try
        {
            using var document = JsonDocument.Parse(output.StandardOutput);
            var result = new List<NetworkNeighborInfo>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryGetString(item, "dev", out var device) ||
                    !string.Equals(device, networkInterface.Name, StringComparison.Ordinal) ||
                    !TryGetString(item, "dst", out var destination) ||
                    destination is null)
                {
                    continue;
                }

                _ = TryGetString(item, "lladdr", out var linkLayerAddress);

                result.Add(new NetworkNeighborInfo
                {
                    IpAddress = destination,
                    LinkLayerAddress = linkLayerAddress ?? "",
                    AddressFamily = IPAddress.TryParse(destination, out var address)
                        ? (int)address.AddressFamily
                        : 0,
                    State = GetNeighborState(item)
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<NetworkNeighborInfo>();
        }
    }

    public async ValueTask<IReadOnlyList<DefaultRouteInfo>> GetDefaultRoutesAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<DefaultRouteInfo>();

        foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
        {
            foreach (var route in await ReadRouteEntriesAsync(family, cancellationToken))
            {
                if (!IsDefaultDestination(route.Destination))
                    continue;

                result.Add(new DefaultRouteInfo
                {
                    DestinationPrefix = family == AddressFamily.InterNetwork ? "0.0.0.0/0" : "::/0",
                    NextHop = route.NextHop,
                    AddressFamily = (int)family,
                    InterfaceIndex = GetInterfaceIndex(route.Device, family),
                    InterfaceAlias = route.Device,
                    RouteMetric = route.Metric,
                    InterfaceMetric = 0
                });
            }
        }

        return result
            .OrderBy(route => route.AddressFamily)
            .ThenBy(route => route.TotalMetric)
            .ThenBy(route => route.InterfaceAlias, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<IReadOnlyList<InterfaceRouteInfo>> ReadRoutesAsync(
        AddressFamily addressFamily,
        string interfaceName,
        CancellationToken cancellationToken)
    {
        var entries = await ReadRouteEntriesAsync(addressFamily, cancellationToken);

        return entries
            .Where(entry => string.Equals(entry.Device, interfaceName, StringComparison.Ordinal))
            .Select(entry => new InterfaceRouteInfo
            {
                DestinationPrefix = NormalizeDestination(entry.Destination, addressFamily),
                NextHop = entry.NextHop,
                AddressFamily = (int)addressFamily,
                RouteMetric = entry.Metric,
                InterfaceMetric = 0
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<RouteEntry>> ReadRouteEntriesAsync(
        AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        var familyFlag = addressFamily == AddressFamily.InterNetwork ? "-4" : "-6";
        var output = await RunCommandAsync(
            "ip",
            ["-j", familyFlag, "route", "show", "table", "all"],
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output?.StandardOutput))
            return Array.Empty<RouteEntry>();

        try
        {
            using var document = JsonDocument.Parse(output.StandardOutput);
            var result = new List<RouteEntry>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryGetString(item, "dev", out var device) || string.IsNullOrWhiteSpace(device))
                    continue;

                _ = TryGetString(item, "dst", out var destination);
                _ = TryGetString(item, "gateway", out var gateway);

                result.Add(new RouteEntry(
                    destination ?? "default",
                    gateway ?? "",
                    device,
                    TryGetInt32(item, "metric") ?? 0));
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<RouteEntry>();
        }
    }

    private static IwLinkInfo? ParseIwLink(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) ||
            output.Contains("Not connected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? ssid = null;
        string? bssid = null;
        string? rateDescription = null;
        int? rssi = null;
        int? frequencyMhz = null;
        double? receiveRate = null;
        double? transmitRate = null;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Connected to ", StringComparison.OrdinalIgnoreCase))
                bssid = line["Connected to ".Length..].Split(' ', 2)[0];
            else if (line.StartsWith("SSID:", StringComparison.OrdinalIgnoreCase))
                ssid = line["SSID:".Length..].Trim();
            else if (line.StartsWith("freq:", StringComparison.OrdinalIgnoreCase))
                frequencyMhz = ParseFirstInt(line["freq:".Length..]);
            else if (line.StartsWith("signal:", StringComparison.OrdinalIgnoreCase))
                rssi = ParseFirstInt(line["signal:".Length..]);
            else if (line.StartsWith("rx bitrate:", StringComparison.OrdinalIgnoreCase))
            {
                rateDescription = line;
                receiveRate = ParseFirstDouble(line["rx bitrate:".Length..]);
            }
            else if (line.StartsWith("tx bitrate:", StringComparison.OrdinalIgnoreCase))
            {
                rateDescription ??= line;
                transmitRate = ParseFirstDouble(line["tx bitrate:".Length..]);
            }
        }

        if (string.IsNullOrWhiteSpace(ssid) || string.IsNullOrWhiteSpace(bssid))
            return null;

        return new IwLinkInfo(
            ssid,
            bssid,
            rssi,
            frequencyMhz is > 0 ? frequencyMhz.Value / 1000d : null,
            FrequencyMhzToChannel(frequencyMhz),
            receiveRate,
            transmitRate,
            rateDescription);
    }

    private static IReadOnlyList<NmcliWifiRow> ParseNmcliWifiRows(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<NmcliWifiRow>();

        var result = new List<NmcliWifiRow>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = SplitEscapedFields(line);
            if (fields.Count < 8 || string.IsNullOrWhiteSpace(fields[1]))
                continue;

            var frequencyMhz = ParseFirstInt(fields[4]);
            result.Add(new NmcliWifiRow(
                fields[0] is "*" or "yes",
                fields[1],
                fields[2],
                ParseFirstInt(fields[3]),
                frequencyMhz is > 0 ? frequencyMhz.Value / 1000d : null,
                ParseFirstInt(fields[5]) ?? FrequencyMhzToChannel(frequencyMhz),
                ParseFirstDouble(fields[6]),
                string.IsNullOrWhiteSpace(fields[7]) ? null : fields[7]));
        }

        return result;
    }

    private static List<VisibleWirelessNetworkInfo> CreateVisibleNetworks(
        IReadOnlyList<NmcliWifiRow> rows)
    {
        return rows
            .GroupBy(row => row.Ssid, StringComparer.Ordinal)
            .Select(group => new VisibleWirelessNetworkInfo
            {
                Ssid = group.Key,
                SignalQualityPercent = group.Max(row => row.SignalQualityPercent),
                Authentication = group.Select(row => row.Security).FirstOrDefault(value => value is not null),
                BssidCount = group.Select(row => row.Bssid).Where(value => value.Length > 0).Distinct().Count(),
                IsConnectable = null
            })
            .OrderByDescending(network => network.SignalQualityPercent)
            .ThenBy(network => network.Ssid, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> SplitEscapedFields(string line)
    {
        var result = new List<string>();
        var current = new List<char>();
        var escaped = false;

        foreach (var character in line)
        {
            if (escaped)
            {
                current.Add(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else if (character == ':')
            {
                result.Add(new string(current.ToArray()));
                current.Clear();
            }
            else
            {
                current.Add(character);
            }
        }

        if (escaped)
            current.Add('\\');

        result.Add(new string(current.ToArray()));
        return result;
    }

    private static string? InferPhyType(string? rateDescription)
    {
        if (string.IsNullOrWhiteSpace(rateDescription))
            return null;
        if (rateDescription.Contains("EHT", StringComparison.OrdinalIgnoreCase))
            return "802.11be";
        if (rateDescription.Contains("HE-", StringComparison.OrdinalIgnoreCase))
            return "802.11ax";
        if (rateDescription.Contains("VHT-", StringComparison.OrdinalIgnoreCase))
            return "802.11ac";
        if (rateDescription.Contains("MCS", StringComparison.OrdinalIgnoreCase))
            return "802.11n";
        return null;
    }

    private static int? FrequencyMhzToChannel(int? frequencyMhz)
    {
        if (frequencyMhz is null or <= 0)
            return null;
        if (frequencyMhz == 2484)
            return 14;
        if (frequencyMhz is >= 2412 and <= 2472)
            return (frequencyMhz.Value - 2407) / 5;
        if (frequencyMhz is >= 5000 and <= 5900)
            return (frequencyMhz.Value - 5000) / 5;
        if (frequencyMhz is >= 5955 and <= 7115)
            return (frequencyMhz.Value - 5950) / 5;
        return null;
    }

    private static async Task<Dictionary<string, string>> ReadOsReleaseAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var line in await File.ReadAllLinesAsync("/etc/os-release", cancellationToken))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                result[line[..separator]] = line[(separator + 1)..].Trim().Trim('"');
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }

    private static async Task<string?> ReadTrimmedTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetDriverName(string interfaceName)
    {
        try
        {
            var driverPath = Path.Combine(SysClassNet, interfaceName, "device", "driver");
            return Directory.ResolveLinkTarget(driverPath, returnFinalTarget: true)?.Name;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int GetInterfaceIndex(string interfaceName, AddressFamily addressFamily)
    {
        try
        {
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => string.Equals(item.Name, interfaceName, StringComparison.Ordinal));
            var properties = networkInterface?.GetIPProperties();

            return addressFamily == AddressFamily.InterNetwork
                ? properties?.GetIPv4Properties()?.Index ?? 0
                : properties?.GetIPv6Properties()?.Index ?? 0;
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    private static async Task<CommandResult?> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);
            var standardOutput = await outputTask;
            var standardError = await errorTask;

            return process.ExitCode == 0
                ? new CommandResult(standardOutput, standardError)
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return value is not null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static string GetNeighborState(JsonElement element)
    {
        if (!element.TryGetProperty("state", out var state))
            return "<unknown>";
        if (state.ValueKind == JsonValueKind.String)
            return state.GetString() ?? "<unknown>";
        if (state.ValueKind == JsonValueKind.Array)
            return string.Join(",", state.EnumerateArray().Select(value => value.GetString()));
        return "<unknown>";
    }

    private static bool IsDefaultDestination(string destination)
    {
        return string.IsNullOrWhiteSpace(destination) ||
               destination is "default" or "0.0.0.0/0" or "::/0";
    }

    private static string NormalizeDestination(string destination, AddressFamily addressFamily)
    {
        if (IsDefaultDestination(destination))
            return addressFamily == AddressFamily.InterNetwork ? "0.0.0.0/0" : "::/0";

        if (IPAddress.TryParse(destination, out var address))
            return $"{destination}/{(address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)}";

        return destination;
    }

    private static bool TryParseHexUInt64(string? value, out ulong result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ulong.TryParse(
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static int? ParseFirstInt(string value)
    {
        var token = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static double? ParseFirstDouble(string value)
    {
        var token = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static uint GetEffectiveUserId()
    {
        try
        {
            return geteuid();
        }
        catch (DllNotFoundException)
        {
            return uint.MaxValue;
        }
    }

    private sealed record CommandResult(string StandardOutput, string StandardError);

    private sealed record RouteEntry(string Destination, string NextHop, string Device, int Metric);

    private sealed record IwLinkInfo(
        string Ssid,
        string Bssid,
        int? RssiDbm,
        double? FrequencyGhz,
        int? Channel,
        double? ReceiveRateMbps,
        double? TransmitRateMbps,
        string? RateDescription);

    private sealed record NmcliWifiRow(
        bool IsActive,
        string Ssid,
        string Bssid,
        int? SignalQualityPercent,
        double? FrequencyGhz,
        int? Channel,
        double? RateMbps,
        string? Security);
}
