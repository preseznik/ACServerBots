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
}
