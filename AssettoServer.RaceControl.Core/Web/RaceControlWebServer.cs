using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Players;
using AssettoServer.RaceControl.Core.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssettoServer.RaceControl.Core.Web;

public sealed class RaceControlWebServer : IAsyncDisposable
{
    public const int ApiVersion = 1;
    private const string ControlHeader = "X-ASRC-Control";
    private const string SessionHeader = "X-ASRC-Session";
    internal const string SessionCookie = "asrc_session";
    private const string SignInCookie = "asrc_signin";
    private const string AccessKey = "asrc.access";
    private const string CallbackPath = "/auth/steam/callback";

    private readonly RaceControlWebServerOptions _options;
    private readonly RaceControlPaths _paths;
    private readonly IRaceControlWebControl _control;
    private readonly PlayerRegistrationStore? _registrations;
    private readonly SteamOpenId _steam;
    private readonly HttpClient? _ownedSteamHttp;
    private readonly Action<string>? _log;
    private readonly string _controlToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private WebApplication? _application;

    public RaceControlWebServer(RaceControlWebServerOptions options, RaceControlPaths paths,
        IRaceControlWebControl control, Action<string>? log = null,
        PlayerRegistrationStore? registrations = null, HttpClient? steamHttp = null, WebSessionStore? sessions = null)
    {
        _options = options;
        _paths = paths;
        _control = control;
        _log = log;
        _registrations = registrations;
        if (steamHttp is null)
        {
            _ownedSteamHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            steamHttp = _ownedSteamHttp;
        }
        _steam = new SteamOpenId(steamHttp);
        Sessions = sessions ?? new WebSessionStore();
    }

    public bool IsRunning => _application is not null;
    public string BrowserUrl => _options.BrowserUrl;
    internal WebSessionStore Sessions { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
            return;
        if (!_options.Enabled)
            return;
        if (!_options.TryValidate(out string error))
            throw new InvalidOperationException(error);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RaceControlWebServer).Assembly.GetName().Name,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel =>
            kestrel.Listen(IPAddress.Parse(_options.BindAddress), _options.Port));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, _shutdown.Token);
            context.RequestAborted = requestLifetime.Token;
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; "
                + "connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
            context.Items[AccessKey] = ResolveAccess(context);
            await next();
        });

        app.MapGet("/", context => WriteAssetAsync(context, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/styles.css", context => WriteAssetAsync(context, "styles.css", "text/css; charset=utf-8"));
        app.MapGet("/app.js", context => WriteAssetAsync(context, "app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/api/v1/health", () => Results.Ok(new
        {
            status = "ok",
            apiVersion = ApiVersion,
            localOwnerOnly = false,
            steamSignIn = true,
        }));
        app.MapGet("/api/v1/status", GetStatus);
        app.MapGet("/api/v1/track", GetTrack);
        app.MapPost("/api/v1/actions/{action}", ExecuteActionAsync);
        app.MapPost("/api/v1/environment", SetEnvironmentAsync);

        app.MapGet("/auth/steam/login", BeginSteamSignIn);
        app.MapGet(CallbackPath, CompleteSteamSignInAsync);
        app.MapPost("/auth/logout", SignOut);
        app.MapGet("/api/v1/me", GetMe);
        app.MapGet("/api/v1/event", GetEvent);
        app.MapPost("/api/v1/registration", RegisterAsync);
        app.MapDelete("/api/v1/registration", Withdraw);

        await app.StartAsync(cancellationToken);
        _application = app;
        _log?.Invoke($"Web GUI listening at {BrowserUrl}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var app = Interlocked.Exchange(ref _application, null);
        if (app is null)
            return;
        _shutdown.Cancel();
        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
        _log?.Invoke("Web GUI stopped.");
    }

    private WebAccess ResolveAccess(HttpContext context)
    {
        var session = Sessions.Find(context.Request.Cookies[SessionCookie]);
        var role = WebAccessPolicy.Resolve(context.Connection.RemoteIpAddress, session?.SteamId, _options.Admins);
        return new WebAccess(role, session);
    }

    private static WebAccess Access(HttpContext context) => (WebAccess)context.Items[AccessKey]!;

    private IResult GetStatus(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var access = Access(context);
        var live = new LiveRaceControlClient(_paths.WorkingInstanceDirectory).TryReadSnapshot();
        return Results.Ok(new
        {
            schemaVersion = ApiVersion,
            generatedAt = DateTimeOffset.UtcNow,
            localOwnerOnly = false,
            role = access.Role.ToString().ToLowerInvariant(),
            webAddress = BrowserUrl,
            // Only admins can operate the server and race; others watch.
            controlToken = access.Role == WebRole.Admin ? _controlToken : null,
            launcher = _control.GetState(),
            live,
        });
    }

    private IResult GetTrack(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var track = new LiveRaceControlClient(_paths.WorkingInstanceDirectory).TryReadTrack();
        return track is null ? Results.NoContent() : Results.Ok(track);
    }

    private async Task<IResult> ExecuteActionAsync(string action, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!IsAdminRequest(context))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!TryParseAction(action, out var parsed))
            return Results.NotFound(new { message = "Unknown Race Control action." });
        if (!await _actionLock.WaitAsync(0, cancellationToken))
            return Results.Conflict(new { message = "Another web action is already running." });

        try
        {
            RaceControlWebActionResult result = await _control.ExecuteAsync(parsed, cancellationToken);
            return result.Accepted ? Results.Ok(result) : Results.Conflict(result);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private async Task<IResult> SetEnvironmentAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!IsAdminRequest(context))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!await _actionLock.WaitAsync(0, cancellationToken))
            return Results.Conflict(new { message = "Another web action is already running." });

        try
        {
            var request = await context.Request.ReadFromJsonAsync<RaceControlWebEnvironmentRequest>(
                cancellationToken: cancellationToken);
            if (request is null)
                return Results.BadRequest(new { message = "Environment request is missing." });
            RaceControlWebActionResult result = await _control.SetEnvironmentAsync(request,
                cancellationToken);
            return result.Accepted ? Results.Ok(result) : Results.Conflict(result);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    /// <summary>Admin role plus the page's control token, so another site can't trigger actions.</summary>
    private bool IsAdminRequest(HttpContext context) =>
        Access(context).Role == WebRole.Admin && HasHeaderToken(context, ControlHeader, _controlToken);

    private static bool HasHeaderToken(HttpContext context, string header, string expected) =>
        context.Request.Headers.TryGetValue(header, out var supplied)
        && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied.ToString()),
            System.Text.Encoding.UTF8.GetBytes(expected));

    // ---- Steam sign-in -------------------------------------------------------------------------

    private IResult BeginSteamSignIn(HttpContext context)
    {
        if (!TryGetOrigin(context, out string origin))
            return Results.BadRequest(new { message = $"Open the Web GUI at {BrowserUrl} to sign in." });
        string state = Sessions.BeginSignIn();
        context.Response.Cookies.Append(SignInCookie, state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/auth/steam",
            MaxAge = WebSessionStore.SignInLifetime,
            IsEssential = true,
        });
        string returnTo = $"{origin}{CallbackPath}?state={Uri.EscapeDataString(state)}";
        return Results.Redirect(SteamOpenId.BuildLoginUrl(returnTo, origin + "/"));
    }

    private async Task<IResult> CompleteSteamSignInAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Cookies.Delete(SignInCookie, new CookieOptions { Path = "/auth/steam" });
        if (!TryGetOrigin(context, out string origin))
            return Results.BadRequest();

        string? state = context.Request.Query["state"];
        if (state is null || state != context.Request.Cookies[SignInCookie] || !Sessions.CompleteSignIn(state))
            return Results.Redirect("/?signin=expired#players");

        var parameters = context.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        string? steamId = await _steam.VerifyAsync(parameters, origin + CallbackPath, cancellationToken);
        if (steamId is null || !Sessions.TryUseNonce(parameters.GetValueOrDefault("openid.response_nonce")))
        {
            _log?.Invoke("Steam sign-in was rejected.");
            return Results.Redirect("/?signin=failed#players");
        }

        var session = Sessions.Create(steamId);
        context.Response.Cookies.Append(SessionCookie, session.Id, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = WebSessionStore.SessionLifetime,
            IsEssential = true,
        });
        _log?.Invoke($"Steam sign-in: {steamId}");
        return Results.Redirect("/#players");
    }

    private IResult SignOut(HttpContext context)
    {
        var access = Access(context);
        if (access.Session is null || !HasHeaderToken(context, SessionHeader, access.Session.CsrfToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        Sessions.Remove(access.Session.Id);
        context.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
        return Results.Ok(new { message = "Signed out." });
    }

    /// <summary>
    /// The address the browser used, accepted only when it is this listener's own address (or localhost
    /// for a loopback listener), so Steam always returns players here.
    /// </summary>
    private bool TryGetOrigin(HttpContext context, out string origin)
    {
        var host = context.Request.Host;
        bool loopbackListener = IPAddress.TryParse(_options.BindAddress, out var bind) && IPAddress.IsLoopback(bind);
        bool knownHost = string.Equals(host.Host, _options.BindAddress, StringComparison.OrdinalIgnoreCase)
                         || (loopbackListener && string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase));
        origin = $"http://{host.Host}:{_options.Port}";
        return knownHost && (host.Port ?? 80) == _options.Port;
    }

    // ---- Players -------------------------------------------------------------------------------

    private IResult GetMe(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var access = Access(context);
        return Results.Ok(new
        {
            role = access.Role.ToString().ToLowerInvariant(),
            signedIn = access.Session is not null,
            steamId = access.SteamId,
            sessionToken = access.Session?.CsrfToken,
            isHostPc = context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote),
        });
    }

    private IResult GetEvent(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var access = Access(context);
        var info = _registrations is null ? null : _control.GetPlayerEvent();
        if (info is null)
            return Results.Ok(new { available = false });

        var registrations = _registrations!.List(info.PresetId);
        var carNames = info.Cars.ToDictionary(car => car.Id, car => car.Name, StringComparer.OrdinalIgnoreCase);
        bool isAdmin = access.Role == WebRole.Admin;
        return Results.Ok(new
        {
            available = true,
            info.EventName,
            info.Track,
            info.Layout,
            info.IsOpen,
            info.ScheduledStart,
            info.Note,
            info.IsTeamEvent,
            info.Cars,
            registeredCount = registrations.Count,
            roster = registrations.OrderBy(registration => registration.RegisteredAt).Select(registration => new
            {
                registration.DisplayName,
                registration.TeamName,
                preferredCar = carNames.GetValueOrDefault(registration.PreferredCarId, registration.PreferredCarId),
                // Steam IDs are only shown to admins, who assign them to cars.
                steamId = isAdmin ? registration.SteamId : null,
                isYou = registration.SteamId == access.SteamId,
            }),
            myRegistration = access.SteamId is { } steamId
                ? registrations.FirstOrDefault(registration => registration.SteamId == steamId)
                : null,
        });
    }

    private async Task<IResult> RegisterAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!TryGetPlayer(context, out var session, out var problem))
            return problem!;
        var info = _control.GetPlayerEvent();
        if (info is not { IsOpen: true })
            return Results.Conflict(new { message = "Registration for this event is closed." });

        PlayerRegistrationRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<PlayerRegistrationRequest>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            request = null;
        }
        if (request is null)
            return Results.BadRequest(new { message = "Registration details are missing." });

        var result = _registrations!.Register(info.PresetId, session!.SteamId, request,
            info.Cars.Select(car => car.Id).ToArray(), DateTimeOffset.UtcNow);
        return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
    }

    private IResult Withdraw(HttpContext context)
    {
        if (!TryGetPlayer(context, out var session, out var problem))
            return problem!;
        var info = _control.GetPlayerEvent();
        if (info is null)
            return Results.Conflict(new { message = "There is no event to withdraw from." });
        bool removed = _registrations!.Remove(info.PresetId, session!.SteamId);
        return Results.Ok(new { message = removed ? "Your registration was withdrawn." : "You weren't registered." });
    }

    /// <summary>A signed-in player (or admin) whose request carries their session token.</summary>
    private bool TryGetPlayer(HttpContext context, out WebSession? session, out IResult? problem)
    {
        session = Access(context).Session;
        problem = null;
        if (_registrations is null)
            problem = Results.NotFound(new { message = "Registration isn't available." });
        else if (session is null)
            problem = Results.Json(new { message = "Sign in through Steam first." }, statusCode: StatusCodes.Status401Unauthorized);
        else if (!HasHeaderToken(context, SessionHeader, session.CsrfToken))
            problem = Results.StatusCode(StatusCodes.Status403Forbidden);
        return problem is null;
    }

    private static bool TryParseAction(string value, out RaceControlWebAction action)
    {
        action = value.ToLowerInvariant() switch
        {
            "launch-server" => RaceControlWebAction.LaunchServer,
            "stop-server" => RaceControlWebAction.StopServer,
            "restart-server" => RaceControlWebAction.RestartServer,
            "start-session" => RaceControlWebAction.StartSession,
            "stop-session" => RaceControlWebAction.StopSession,
            "restart-session" => RaceControlWebAction.RestartSession,
            _ => (RaceControlWebAction)(-1),
        };
        return Enum.IsDefined(action);
    }

    private static async Task WriteAssetAsync(HttpContext context, string filename, string contentType)
    {
        context.Response.ContentType = contentType;
        context.Response.Headers.CacheControl = "no-cache";
        string resourceName = $"AssettoServer.RaceControl.Core.Assets.Web.{filename}";
        await using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdown.Dispose();
        _actionLock.Dispose();
        _ownedSteamHttp?.Dispose();
    }
}
