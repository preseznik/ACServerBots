using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Network;

public static class NetworkAddressService
{
    public static IReadOnlyList<string> GetPrivateIpv4Addresses()
    {
        var addresses = new List<string>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork
                    && RaceControlValidator.TryPrivateAddress(address.ToString(), out var isLoopback)
                    && !isLoopback)
                {
                    addresses.Add(address.ToString());
                }
            }
        }

        return addresses.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address.StartsWith("192.168.", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(address => address, StringComparer.OrdinalIgnoreCase)
            .Append(IPAddress.Loopback.ToString())
            .ToArray();
    }

    public static string GetPreferredPrivateIpv4() => GetPrivateIpv4Addresses().FirstOrDefault() ?? IPAddress.Loopback.ToString();
}
