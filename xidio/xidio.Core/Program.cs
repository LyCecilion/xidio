using System.Net;
using System.Net.NetworkInformation;

namespace xidio.Core
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===== xidio v0.1.0 =====\n");
            Console.WriteLine("We're still developing xidio, so a lot of features could be changed in later versions.");
            Console.WriteLine("Made by LyCecilion, along with xilin, with our love.");

            // Timestamp
            var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Console.WriteLine($"Current timestamp: {unixSeconds}");
            Console.WriteLine("");

            // Hostname
            var hostName = Dns.GetHostName();
            Console.WriteLine($"Hostname: {hostName}");

            // IP Address
            var addresses = Dns.GetHostEntry(hostName).AddressList;
            foreach (var ipaddr in addresses)
                Console.WriteLine(ipaddr);

            // System Proxy
            var handler = new HttpClientHandler();
            var defaultProxy = handler.Proxy ?? WebRequest.GetSystemWebProxy();
            var proxyUri = defaultProxy?.GetProxy(new Uri("https://xidio.stellalyr.ink"));
            if (proxyUri != null && proxyUri != new Uri("https://xidio.stellalyr.ink"))
                Console.WriteLine($"System proxy is on. {proxyUri}");
            else
                Console.WriteLine("System proxy is off.");

            // NetworkInterface
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

            // DNS
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var ipProps = ni.GetIPProperties();
                var dnsAddresses = ipProps.DnsAddresses;
                if (dnsAddresses.Count <= 0) continue;
                Console.WriteLine($"Adapter: {ni.Name}");
                foreach (var dns in dnsAddresses)
                    Console.WriteLine($"  DNS Server: {dns}");
            }

        }
    }
}
