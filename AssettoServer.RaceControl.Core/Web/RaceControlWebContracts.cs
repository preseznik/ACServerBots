namespace AssettoServer.RaceControl.Core.Web;

public enum RaceControlWebAction
{
    LaunchServer,
    StopServer,
    RestartServer,
    StartSession,
    StopSession,
    RestartSession,
}

public sealed record RaceControlWebControlState(
    string EventName,
    string ServerName,
    string Mode,
    string SessionLabel,
    string Track,
    string Layout,
    string FpsTheme,
    string ServerState,
    string Status,
    bool IsBusy,
    bool CanLaunchServer,
    bool CanStopServer,
    bool CanRestartServer,
    bool CanStartSession,
    bool CanStopSession,
    bool CanRestartSession,
    string FpsMatchType = "",
    string FpsMutators = "",
    string FpsStartPolicy = "",
    string JoinSummary = "",
    string JoinAddress = "",
    string JoinLink = "");

public sealed record RaceControlWebActionResult(bool Accepted, string Message)
{
    public static RaceControlWebActionResult Rejected(string message) => new(false, message);
    public static RaceControlWebActionResult Success(string message) => new(true, message);
}

public sealed record RaceControlWebEnvironmentRequest(int WeatherType, int TimeOfDaySeconds);

public sealed record PlayerEventCar(string Id, string Name);

/// <summary>One grid row on the Web GUI's Overview page.</summary>
public sealed record WebOverviewEntry(int Number, string DriverName, string TeamName, string CarName,
    string? CarImageKey, bool IsLive, string Role);

public sealed record WebOverviewCar(string Name, string? ImageKey, int Count);

public sealed record WebOverviewTeam(string Name, IReadOnlyList<string> Drivers, int Cars);

/// <summary>
/// What the Overview page shows. Image keys are resolved back to files by <see cref="IRaceControlWebControl.GetMediaPath"/>;
/// the browser never supplies a file path.
/// </summary>
public sealed record WebOverview(
    string EventName,
    string Mode,
    string Track,
    string Layout,
    string Country,
    string? TrackImageKey,
    string? TrackOutlineKey,
    IReadOnlyList<WebOverviewCar> Cars,
    IReadOnlyList<WebOverviewTeam> Teams,
    IReadOnlyList<WebOverviewEntry> Entries);

/// <summary>The event players can register for on the Players page: the preset open in Race Control.</summary>
public sealed record PlayerEventInfo(
    Guid PresetId,
    string EventName,
    string Track,
    string Layout,
    bool IsOpen,
    DateTimeOffset? ScheduledStart,
    string Note,
    IReadOnlyList<PlayerEventCar> Cars,
    bool IsTeamEvent);

public interface IRaceControlWebControl
{
    RaceControlWebControlState GetState();
    Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
        CancellationToken cancellationToken = default);
    Task<RaceControlWebActionResult> SetEnvironmentAsync(RaceControlWebEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The current event for the Players page, or null when Race Control has none.</summary>
    PlayerEventInfo? GetPlayerEvent() => null;

    /// <summary>Event, track, cars, teams and entry list for the Overview page.</summary>
    WebOverview? GetOverview() => null;

    /// <summary>The image file behind an overview image key, or null. Only content the app knows about.</summary>
    string? GetMediaPath(string key) => null;
}
