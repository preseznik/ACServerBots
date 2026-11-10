using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Network;

public static class NetworkAddressService
{
    /// <summary>LAN addresses plus loopback. Used for listeners without login, such as the Web GUI.</summary>
    public static IReadOnlyList<string> GetPrivateIpv4Addresses()
        => OrderHostAddresses(GetInterfaceAddresses().Where(address =>
            RaceControlValidator.TryPrivateAddress(address, out var isLoopback) && !isLoopback));

    /// <summary>
    /// Addresses the game server can listen on: LAN addresses first, then VPN overlay addresses
    /// (Tailscale and similar, 100.64.0.0/10), then loopback.
    /// </summary>
    public static IReadOnlyList<string> GetServerIpv4Addresses()
        => OrderHostAddresses(GetInterfaceAddresses().Where(address =>
            RaceControlValidator.TryHostAddress(address, out var isLoopback, out _) && !isLoopback));

    public static string GetPreferredPrivateIpv4() => GetPrivateIpv4Addresses().FirstOrDefault() ?? IPAddress.Loopback.ToString();

    public static IReadOnlyList<string> OrderHostAddresses(IEnumerable<string> addresses)
        => addresses.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address.StartsWith("192.168.", StringComparison.Ordinal) ? 0
                : RaceControlValidator.IsVpnOverlayAddress(address) ? 2
                : 1)
            .ThenBy(address => address, StringComparer.OrdinalIgnoreCase)
            .Append(IPAddress.Loopback.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> GetInterfaceAddresses()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return unicast.Address.ToString();
            }
        }
    }
}
