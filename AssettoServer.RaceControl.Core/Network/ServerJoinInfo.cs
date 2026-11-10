using System.Net;
using System.Net.Sockets;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Network;

/// <summary>
/// How players reach a running server: its bind address and HTTP port, which Content Manager uses to
/// join directly when LAN discovery can't find the server (another subnet, a VPN).
/// </summary>
public sealed record ServerJoinInfo(string Address, int HttpPort)
{
    public string Endpoint => $"{Address}:{HttpPort}";

    public bool IsLoopback => RaceControlValidator.TryPrivateAddress(Address, out var isLoopback) && isLoopback;

    public bool IsVpnOverlay => RaceControlValidator.IsVpnOverlayAddress(Address);

    /// <summary>Opens Content Manager's join dialog for this server. No password is included.</summary>
    public string ContentManagerLink
        => $"acmanager://race/online/join?ip={Uri.EscapeDataString(Address)}&httpPort={HttpPort}";

    public string Summary => IsLoopback
        ? $"Only this PC can join at {Endpoint}. Pick the LAN address on the Network tab for other PCs."
        : IsVpnOverlay
            ? $"Players on your VPN join at {Endpoint}."
            : $"Players join at {Endpoint}.";

    public static ServerJoinInfo? Create(string? bindAddress, int httpPort)
    {
        if (httpPort is < 1 or > 65535
            || !IPAddress.TryParse(bindAddress?.Trim(), out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || address.Equals(IPAddress.Any))
        {
            return null;
        }

        return new ServerJoinInfo(address.ToString(), httpPort);
    }
}
