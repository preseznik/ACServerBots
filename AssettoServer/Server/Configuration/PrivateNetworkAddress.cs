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

    /// <summary>
    /// Shared address space 100.64.0.0/10 (RFC 6598), used by VPN overlays such as Tailscale.
    /// It is never publicly routed, so only devices on the same VPN can reach the server.
    /// </summary>
    public static bool IsVpnOverlayIpv4(string value)
        => IPAddress.TryParse(value, out var address)
           && address.GetAddressBytes() is [100, >= 64 and <= 127, _, _];

    /// <summary>Race and FPS listeners: private LAN or VPN overlay, never a wildcard or public address.</summary>
    public static bool IsLanOrVpnIpv4(string value) => IsPrivateIpv4(value) || IsVpnOverlayIpv4(value);
}
