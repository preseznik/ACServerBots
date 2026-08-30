using System.Net;
using System.Net.Sockets;
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

        Assert.Multiple(() =>
        {
            Assert.That(index, Does.Contain("AssettoServer Race Control"));
            Assert.That(index, Does.Contain("LIVE SESSION"));
            Assert.That(status.RootElement.GetProperty("localOwnerOnly").GetBoolean(), Is.True);
            Assert.That(status.RootElement.GetProperty("live").GetProperty("sequence").GetInt64(), Is.EqualTo(42));
            Assert.That(status.RootElement.GetProperty("launcher").GetProperty("eventName").GetString(), Is.EqualTo("Test Deathmatch"));
            Assert.That(track.RootElement.GetProperty("track").GetString(), Is.EqualTo("fire_pit"));
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(control.LastAction, Is.EqualTo(RaceControlWebAction.StartSession));
        });
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

        public RaceControlWebControlState GetState() => new(
            "Test Deathmatch", "Test Server", "FPS", "MATCH", "fire_pit", "",
            "RUNNING", "Ready", false, false, true, true, true, true, true);

        public Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
            CancellationToken cancellationToken = default)
        {
            LastAction = action;
            return Task.FromResult(RaceControlWebActionResult.Success("Accepted."));
        }
    }
}
