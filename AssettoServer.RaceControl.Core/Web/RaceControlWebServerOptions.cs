using System.Net;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Web;

/// <param name="AdminSteamIds">
/// SteamID64s that get admin controls when signed in from another device. The host PC itself is always admin.
/// </param>
public sealed record RaceControlWebServerOptions(bool Enabled, string BindAddress, int Port,
    IReadOnlyCollection<string>? AdminSteamIds = null)
{
    public string ListenerUrl => $"http://{BindAddress}:{Port}";
    public string BrowserUrl => ListenerUrl + "/";

    public IReadOnlyCollection<string> Admins { get; } = (AdminSteamIds ?? [])
        .Where(SteamIds.IsValid).Distinct(StringComparer.Ordinal).ToArray();

    public bool TryValidate(out string error)
    {
        if (!Enabled)
        {
            error = string.Empty;
            return true;
        }

        // Loopback, LAN or a VPN overlay such as Tailscale: never the 0.0.0.0 wildcard or a public address.
        // Controls are admin-only (host PC or listed Steam IDs); other devices can only watch and register.
        if (!IPAddress.TryParse(BindAddress, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !RaceControlValidator.TryHostAddress(address.ToString(), out _, out _))
        {
            error = "Web GUI address must be loopback, a private LAN address or a VPN (100.64.0.0/10) address.";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            error = "Web GUI port must be between 1 and 65535.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
