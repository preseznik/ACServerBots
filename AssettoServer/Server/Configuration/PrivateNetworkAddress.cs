using System.Net;

namespace AssettoServer.Server.Configuration;

public static class PrivateNetworkAddress
{
    public static bool IsValid(string value) => IPAddress.TryParse(value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    public static bool IsPrivateIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            return false;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168));
    }
}
