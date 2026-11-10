using System.Text;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Players;

/// <summary>A player who signed in with Steam and registered for an event.</summary>
public sealed class PlayerRegistration
{
    public string SteamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string PreferredCarId { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PlayerRegistration Copy() => (PlayerRegistration)MemberwiseClone();
}

public sealed record PlayerRegistrationRequest(string? DisplayName, string? TeamName, string? PreferredCarId);

public sealed record PlayerRegistrationResult(bool Accepted, string Message, PlayerRegistration? Registration = null)
{
    public static PlayerRegistrationResult Rejected(string message) => new(false, message);
}

/// <summary>
/// Registrations per event (preset id), kept in Data\Registrations\&lt;preset id&gt;.json. Shared by the
/// Web GUI, where players register, and the desktop Grid, where the host assigns them to cars.
/// Thread-safe: the web listener and the UI thread both use it.
/// </summary>
public sealed class PlayerRegistrationStore
{
    public const int MaximumNameLength = 32;
    public const int MaximumRegistrations = 254;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly RaceControlPaths _paths;
    private readonly Lock _lock = new();

    public PlayerRegistrationStore(RaceControlPaths paths) => _paths = paths;

    /// <summary>Raised after an event's registrations change, with that event's preset id. May fire on any thread.</summary>
    public event EventHandler<Guid>? Changed;

    public string Directory => Path.Combine(_paths.DataRoot, "Registrations");

    public IReadOnlyList<PlayerRegistration> List(Guid presetId)
    {
        lock (_lock)
            return Load(presetId).Select(registration => registration.Copy()).ToArray();
    }

    public PlayerRegistration? Find(Guid presetId, string steamId)
    {
        lock (_lock)
            return Load(presetId).FirstOrDefault(registration => registration.SteamId == steamId)?.Copy();
    }

    /// <summary>Adds or updates the signed-in player's registration.</summary>
    /// <param name="allowedCarIds">Cars on the event's grid; an empty preference is always allowed.</param>
    public PlayerRegistrationResult Register(Guid presetId, string steamId, PlayerRegistrationRequest request,
        IReadOnlyCollection<string> allowedCarIds, DateTimeOffset now)
    {
        if (!SteamIds.IsValid(steamId))
            return PlayerRegistrationResult.Rejected("Sign in with Steam first.");
        string displayName = CleanName(request.DisplayName);
        string teamName = CleanName(request.TeamName);
        string carId = (request.PreferredCarId ?? string.Empty).Trim();
        if (displayName.Length == 0)
            return PlayerRegistrationResult.Rejected("Enter the name to show on the grid.");
        if (carId.Length > 0 && !allowedCarIds.Contains(carId, StringComparer.OrdinalIgnoreCase))
            return PlayerRegistrationResult.Rejected("Choose one of the event's cars.");

        PlayerRegistration saved;
        lock (_lock)
        {
            var registrations = Load(presetId);
            var existing = registrations.FirstOrDefault(registration => registration.SteamId == steamId);
            if (existing is null)
            {
                if (registrations.Count >= MaximumRegistrations)
                    return PlayerRegistrationResult.Rejected("This event is full.");
                existing = new PlayerRegistration { SteamId = steamId, RegisteredAt = now };
                registrations.Add(existing);
            }
            existing.DisplayName = displayName;
            existing.TeamName = teamName;
            existing.PreferredCarId = carId;
            existing.UpdatedAt = now;
            Save(presetId, registrations);
            saved = existing.Copy();
        }

        Changed?.Invoke(this, presetId);
        return new PlayerRegistrationResult(true, "You're registered.", saved);
    }

    public bool Remove(Guid presetId, string steamId)
    {
        lock (_lock)
        {
            var registrations = Load(presetId);
            if (registrations.RemoveAll(registration => registration.SteamId == steamId) == 0)
                return false;
            Save(presetId, registrations);
        }

        Changed?.Invoke(this, presetId);
        return true;
    }

    /// <summary>Keeps names printable and short; they end up in entry_list.ini and on the grid.</summary>
    public static string CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var clean = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            if (!char.IsControl(character) && character is not ('[' or ']' or ';' or '=' or '"'))
                clean.Append(character);
        }
        string result = clean.ToString().Trim();
        return result.Length > MaximumNameLength ? result[..MaximumNameLength].TrimEnd() : result;
    }

    private string PathFor(Guid presetId) => Path.Combine(Directory, $"{presetId:N}.json");

    private List<PlayerRegistration> Load(Guid presetId)
    {
        string path = PathFor(presetId);
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PlayerRegistration>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // A damaged file must not block registration; keep it for inspection.
            File.Copy(path, path + ".damaged", true);
            return [];
        }
    }

    private void Save(Guid presetId, List<PlayerRegistration> registrations)
    {
        _paths.RequireInsidePackage(Directory);
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(presetId);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(registrations, JsonOptions));
        File.Move(temporary, path, true);
    }
}
