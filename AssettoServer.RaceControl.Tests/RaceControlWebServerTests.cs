using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Runtime;
using AssettoServer.RaceControl.Core.Web;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

[TestFixture]
public sealed class RaceControlWebServerTests
{
    [Test]
    public void OptionsAcceptPrivateAddressesAndRejectPublicExposure()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new RaceControlWebServerOptions(true, "127.0.0.1", 8772)
                .TryValidate(out _), Is.True);
            Assert.That(new RaceControlWebServerOptions(true, "192.168.1.25", 8772)
                .TryValidate(out _), Is.True);
            Assert.That(new RaceControlWebServerOptions(true, "8.8.8.8", 8772)
                .TryValidate(out _), Is.False);
            Assert.That(new RaceControlWebServerOptions(true, "0.0.0.0", 8772)
                .TryValidate(out _), Is.False);
            Assert.That(new RaceControlWebServerOptions(true, "127.0.0.1", 0)
                .TryValidate(out _), Is.False);
        });
    }

    [Test]
    public async Task ServesDashboardTelemetryAndProtectedActions()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        paths.EnsureCreated();
        var live = new LiveRaceControlClient(paths.WorkingInstanceDirectory);
        Directory.CreateDirectory(live.ControlDirectory);
        await File.WriteAllTextAsync(live.SnapshotPath, """
        {
          "schemaVersion": 2,
          "sequence": 42,
          "serverRunning": true,
          "isFps": true,
          "session": { "name": "Current match", "type": "Deathmatch", "phase": "running",
            "timeLeftMilliseconds": 90000, "killLimit": 20 },
          "cars": [{ "sessionId": 1, "name": "Operative 02", "isBot": true,
            "isActive": true, "x": 1, "z": 2, "health": 80, "kills": 3, "deaths": 1 }]
        }
        """);
        await File.WriteAllTextAsync(live.TrackPath, """
        {
          "schemaVersion": 2, "track": "fire_pit", "isFpsArena": true,
          "minimumX": -10, "maximumX": 10, "minimumZ": -8, "maximumZ": 8,
          "arenaCellSize": 0.6, "arenaCells": [{ "x": 0, "z": 0 }], "points": []
        }
        """);

        int port = ReserveLoopbackPort();
        var control = new StubWebControl();
        await using var server = new RaceControlWebServer(
            new RaceControlWebServerOptions(true, "127.0.0.1", port), paths, control);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.BrowserUrl) };

        string index = await client.GetStringAsync("/");
        using JsonDocument status = JsonDocument.Parse(await client.GetStringAsync("api/v1/status"));
        using JsonDocument track = JsonDocument.Parse(await client.GetStringAsync("api/v1/track"));
        var forbidden = await client.PostAsync("api/v1/actions/start-session", new ByteArrayContent([]));
        string token = status.RootElement.GetProperty("controlToken").GetString()!;
        client.DefaultRequestHeaders.Add("X-ASRC-Control", token);
        var accepted = await client.PostAsync("api/v1/actions/start-session", new ByteArrayContent([]));
        var environment = await client.PostAsJsonAsync("api/v1/environment",
            new RaceControlWebEnvironmentRequest(19, 18 * 60 * 60));

        Assert.Multiple(() =>
        {
            Assert.That(index, Does.Contain("AssettoServer Race Control"));
            Assert.That(index, Does.Contain("LIVE SESSION"));
            Assert.That(index, Does.Contain("id=\"environment-panel\""));
            Assert.That(index, Does.Contain("id=\"selected-player-panel\""));
            Assert.That(index, Does.Contain("class=\"map-card panel collapsible\""));
            Assert.That(status.RootElement.GetProperty("localOwnerOnly").GetBoolean(), Is.True);
            Assert.That(status.RootElement.GetProperty("live").GetProperty("sequence").GetInt64(), Is.EqualTo(42));
            Assert.That(status.RootElement.GetProperty("launcher").GetProperty("eventName").GetString(), Is.EqualTo("Test Deathmatch"));
            Assert.That(track.RootElement.GetProperty("track").GetString(), Is.EqualTo("fire_pit"));
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(environment.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(control.LastAction, Is.EqualTo(RaceControlWebAction.StartSession));
            Assert.That(control.LastEnvironment,
                Is.EqualTo(new RaceControlWebEnvironmentRequest(19, 18 * 60 * 60)));
        });
    }

    [Test]
    public async Task StopCancelsAnInFlightDashboardActionPromptly()
    {
        using var factory = new TestContentFactory();
        var paths = new RaceControlPaths(factory.DataRoot);
        paths.EnsureCreated();
        int port = ReserveLoopbackPort();
        var control = new BlockingWebControl();
        await using var server = new RaceControlWebServer(
            new RaceControlWebServerOptions(true, "127.0.0.1", port), paths, control);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.BrowserUrl) };
        using JsonDocument status = JsonDocument.Parse(await client.GetStringAsync("api/v1/status"));
        client.DefaultRequestHeaders.Add("X-ASRC-Control",
            status.RootElement.GetProperty("controlToken").GetString()!);

        Task<HttpResponseMessage> request = client.PostAsync(
            "api/v1/actions/start-session", new ByteArrayContent([]));
        await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        await server.StopAsync();
        stopwatch.Stop();
        try
        {
            using var _ = await request;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or TaskCanceledException)
        {
            // The server intentionally aborted the request during application shutdown.
        }

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
            "A dashboard request must not retain the desktop process during shutdown");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubWebControl : IRaceControlWebControl
    {
        public RaceControlWebAction? LastAction { get; private set; }
        public RaceControlWebEnvironmentRequest? LastEnvironment { get; private set; }

        public RaceControlWebControlState GetState() => new(
            "Test Deathmatch", "Test Server", "FPS", "MATCH", "fire_pit", "", "Blocks",
            "RUNNING", "Ready", false, false, true, true, true, true, true);

        public Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
            CancellationToken cancellationToken = default)
        {
            LastAction = action;
            return Task.FromResult(RaceControlWebActionResult.Success("Accepted."));
        }

        public Task<RaceControlWebActionResult> SetEnvironmentAsync(
            RaceControlWebEnvironmentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastEnvironment = request;
            return Task.FromResult(RaceControlWebActionResult.Success("Environment accepted."));
        }
    }

    private sealed class BlockingWebControl : IRaceControlWebControl
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RaceControlWebControlState GetState() => new(
            "Shutdown test", "Test Server", "FPS", "MATCH", "fire_pit", "", "Blocks",
            "RUNNING", "Ready", false, false, true, true, true, true, true);

        public async Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RaceControlWebActionResult.Success("Unreachable");
        }

        public Task<RaceControlWebActionResult> SetEnvironmentAsync(
            RaceControlWebEnvironmentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RaceControlWebActionResult.Success("Accepted."));
    }
}
