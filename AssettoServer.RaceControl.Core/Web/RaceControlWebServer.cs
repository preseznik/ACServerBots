using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using AssettoServer.RaceControl.Core.Infrastructure;
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
    private readonly RaceControlWebServerOptions _options;
    private readonly RaceControlPaths _paths;
    private readonly IRaceControlWebControl _control;
    private readonly Action<string>? _log;
    private readonly string _controlToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private WebApplication? _application;

    public RaceControlWebServer(RaceControlWebServerOptions options, RaceControlPaths paths,
        IRaceControlWebControl control, Action<string>? log = null)
    {
        _options = options;
        _paths = paths;
        _control = control;
        _log = log;
    }

    public bool IsRunning => _application is not null;
    public string BrowserUrl => _options.BrowserUrl;

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
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; "
                + "connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
            await next();
        });

        app.MapGet("/", context => WriteAssetAsync(context, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/styles.css", context => WriteAssetAsync(context, "styles.css", "text/css; charset=utf-8"));
        app.MapGet("/app.js", context => WriteAssetAsync(context, "app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/api/v1/health", () => Results.Ok(new
        {
            status = "ok",
            apiVersion = ApiVersion,
            localOwnerOnly = true,
        }));
        app.MapGet("/api/v1/status", GetStatus);
        app.MapGet("/api/v1/track", GetTrack);
        app.MapPost("/api/v1/actions/{action}", ExecuteActionAsync);

        await app.StartAsync(cancellationToken);
        _application = app;
        _log?.Invoke($"Web GUI listening at {BrowserUrl}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var app = Interlocked.Exchange(ref _application, null);
        if (app is null)
            return;
        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
        _log?.Invoke("Web GUI stopped.");
    }

    private IResult GetStatus(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var live = new LiveRaceControlClient(_paths.WorkingInstanceDirectory).TryReadSnapshot();
        return Results.Ok(new
        {
            schemaVersion = ApiVersion,
            generatedAt = DateTimeOffset.UtcNow,
            localOwnerOnly = true,
            webAddress = BrowserUrl,
            controlToken = _controlToken,
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
        if (!context.Request.Headers.TryGetValue(ControlHeader, out var supplied)
            || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(supplied.ToString()),
                System.Text.Encoding.UTF8.GetBytes(_controlToken)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

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
        _actionLock.Dispose();
    }
}
