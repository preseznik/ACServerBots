using System.Net;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Web;

public sealed record RaceControlWebServerOptions(bool Enabled, string BindAddress, int Port)
{
    public string ListenerUrl => $"http://{BindAddress}:{Port}";
    public string BrowserUrl => ListenerUrl + "/";

    public bool TryValidate(out string error)
    {
        if (!Enabled)
        {
            error = string.Empty;
            return true;
        }

        if (!IPAddress.TryParse(BindAddress, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !RaceControlValidator.TryPrivateAddress(address.ToString(), out _))
        {
            error = "Web GUI address must be loopback or a private IPv4 address.";
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
