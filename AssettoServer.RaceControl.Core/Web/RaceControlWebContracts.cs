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
    string ServerState,
    string Status,
    bool IsBusy,
    bool CanLaunchServer,
    bool CanStopServer,
    bool CanRestartServer,
    bool CanStartSession,
    bool CanStopSession,
    bool CanRestartSession);

public sealed record RaceControlWebActionResult(bool Accepted, string Message)
{
    public static RaceControlWebActionResult Rejected(string message) => new(false, message);
    public static RaceControlWebActionResult Success(string message) => new(true, message);
}

public sealed record RaceControlWebEnvironmentRequest(int WeatherType, int TimeOfDaySeconds);

public interface IRaceControlWebControl
{
    RaceControlWebControlState GetState();
    Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
        CancellationToken cancellationToken = default);
    Task<RaceControlWebActionResult> SetEnvironmentAsync(RaceControlWebEnvironmentRequest request,
        CancellationToken cancellationToken = default);
}
