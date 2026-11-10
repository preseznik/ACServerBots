using System.Text.RegularExpressions;

namespace AssettoServer.RaceControl.Core.Web;

/// <summary>
/// "Sign in through Steam" (OpenID 2.0). The player's browser goes to Steam and comes back with a
/// signed assertion of their SteamID64, which is checked with Steam directly. No Steam password or
/// Web API key is involved, but the host needs internet access to reach steamcommunity.com.
/// </summary>
public sealed partial class SteamOpenId(HttpClient http)
{
    public const string Endpoint = "https://steamcommunity.com/openid/login";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    [GeneratedRegex(@"^https://steamcommunity\.com/openid/id/(7656119\d{10})$", RegexOptions.CultureInvariant)]
    private static partial Regex ClaimedId();

    public static string BuildLoginUrl(string returnTo, string realm)
    {
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };
        return Endpoint + "?" + string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    /// <summary>
    /// Checks Steam's answer on the callback and returns the SteamID64, or null when it isn't genuine.
    /// </summary>
    /// <param name="parameters">All query parameters of the callback request.</param>
    /// <param name="expectedReturnTo">The callback URL this request arrived at, without its query.</param>
    public async Task<string?> VerifyAsync(IReadOnlyDictionary<string, string> parameters, string expectedReturnTo,
        CancellationToken cancellationToken = default)
    {
        string? steamId = CheckAssertion(parameters, expectedReturnTo);
        if (steamId is null)
            return null;

        // Ask Steam to confirm its own signature over the signed fields.
        var verification = parameters
            .Where(pair => pair.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        verification["openid.mode"] = "check_authentication";
        try
        {
            using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(verification), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Split('\n').Any(line => line.Trim() == "is_valid:true") ? steamId : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Local checks that don't need Steam: mode, endpoint, return address and identity.</summary>
    internal static string? CheckAssertion(IReadOnlyDictionary<string, string> parameters, string expectedReturnTo)
    {
        string Get(string key) => parameters.TryGetValue(key, out var value) ? value : string.Empty;

        if (Get("openid.mode") != "id_res"
            || Get("openid.op_endpoint") != Endpoint
            || Get("openid.claimed_id") != Get("openid.identity")
            || !Get("openid.signed").Split(',').Contains("return_to"))
        {
            return null;
        }

        if (!Uri.TryCreate(Get("openid.return_to"), UriKind.Absolute, out var returnTo)
            || !string.Equals(returnTo.GetLeftPart(UriPartial.Path), expectedReturnTo, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = ClaimedId().Match(Get("openid.claimed_id"));
        return match.Success ? match.Groups[1].Value : null;
    }
}
