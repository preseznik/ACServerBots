using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Players;
using AssettoServer.RaceControl.Core.Web;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class OverviewAndGridTests
{
    [Test]
    public void EffectiveSlotOrder_PutsRacingRowsFirstAndCapsThem()
    {
        GridSlotPreset Row(SlotMode mode) => new() { Mode = mode };
        var grid = new[]
        {
            Row(SlotMode.Spectator), // 0
            Row(SlotMode.Auto),      // 1
            Row(SlotMode.None),      // 2
            Row(SlotMode.Spectator), // 3
            Row(SlotMode.Fixed),     // 4
            Row(SlotMode.Auto),      // 5
        };

        Assert.Multiple(() =>
        {
            Assert.That(ServerConfigurationRenderer.EffectiveSlotOrder(grid, EventMode.Racing, 24),
                Is.EqualTo(new[] { 1, 2, 4, 5, 0, 3 }), "session id n is the n-th entry");
            Assert.That(ServerConfigurationRenderer.EffectiveSlotOrder(grid, EventMode.Racing, 3),
                Is.EqualTo(new[] { 1, 2, 4, 0, 3 }), "racing rows beyond the pit boxes are dropped");
        });
    }

    [Test]
    public void Render_ReportsWhichPresetRowEachEntryCameFrom()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(4, true, "car_one");
        var preset = factory.CreatePreset(3);
        preset.Grid.Insert(0, new GridSlotPreset { CarId = "car_one", Mode = SlotMode.Spectator, DriverName = "Camera" });

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.SourceSlotIndexes, Is.EqualTo(new[] { 1, 2, 3, 0 }));
            Assert.That(rendered.EffectiveGrid[^1].DriverName, Is.EqualTo("Camera"));
        });
    }

    [Test]
    public void TeamCars_EachTeamNameOnOneCarExceptTheBotsLabel()
    {
        var rows = new[]
        {
            new TeamCarRow(1, "Night Owls", false),
            new TeamCarRow(2, "night owls ", false),
            new TeamCarRow(3, "Race Control", false),
            new TeamCarRow(4, "Race Control", false),
            new TeamCarRow(5, "Night Owls", true),
            new TeamCarRow(6, "", false),
            new TeamCarRow(7, "", false),
        };

        var conflicts = TeamCars.Conflicts(rows);

        Assert.Multiple(() =>
        {
            Assert.That(conflicts.Single().Team, Is.EqualTo("Night Owls"));
            Assert.That(conflicts.Single().Cars, Is.EqualTo(new[] { 1, 2 }), "names match ignoring case and spaces; spectators don't count");
            Assert.That(TeamCars.ConflictFor(new TeamCarRow(9, "Night Owls", false), rows), Is.EqualTo(1));
            Assert.That(TeamCars.ConflictFor(new TeamCarRow(9, "Race Control", false), rows), Is.Null);
            Assert.That(TeamCars.ConflictFor(new TeamCarRow(9, "Speedy", false), rows), Is.Null);
        });
    }

    [Test]
    public void Validate_RefusesOneTeamOnTwoCarsOnlyWithDriverChanges()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(4, true, "car_one");
        var preset = factory.CreatePreset(3);
        preset.Grid[0].TeamName = "Night Owls";
        preset.Grid[0].DriverSteamIds = ["76561198000000001", "76561198000000002"];
        preset.Grid[1].TeamName = "Night Owls";

        preset.Sessions.DriverSwapGraceMinutes = 5;
        var withChanges = new AssettoServer.RaceControl.Core.Validation.RaceControlValidator().Validate(preset, factory.Scan());
        preset.Sessions.DriverSwapGraceMinutes = 0;
        var withoutChanges = new AssettoServer.RaceControl.Core.Validation.RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(withChanges.IsValid, Is.False);
            Assert.That(withChanges.Messages, Has.Some.Matches<AssettoServer.RaceControl.Core.Validation.ValidationMessage>(message =>
                message.Message.Contains("\"Night Owls\" is on cars 1, 2", StringComparison.Ordinal)));
            Assert.That(withoutChanges.Messages.Select(message => message.Message), Has.None.Contains("is on cars"));
        });
    }

    [TestCase("20:00", 20, 0)]
    [TestCase("9:30", 9, 30)]
    [TestCase("09:30", 9, 30)]
    [TestCase("2030", 20, 30)]
    [TestCase("930", 9, 30)]
    [TestCase(" 20.30 ", 20, 30)]
    public void StartTime_AcceptsCommonWaysOfTypingIt(string text, int hours, int minutes)
    {
        Assert.That(StartTimeText.TryParse(text, out var time), Is.True);
        Assert.That(time, Is.EqualTo(new TimeSpan(hours, minutes, 0)));
    }

    [TestCase("8pm")]
    [TestCase("24:00")]
    [TestCase("20:75")]
    [TestCase("")]
    [TestCase(null)]
    public void StartTime_RejectsOtherText(string? text)
        => Assert.That(StartTimeText.TryParse(text, out _), Is.False);

    [Test]
    public async Task WebGui_ServesOverviewAndOnlyKnownImages()
    {
        string root = Path.Combine(Path.GetTempPath(), $"overview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new RaceControlPaths(root);
            paths.EnsureCreated();
            string trackImage = Path.Combine(root, "preview.png");
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
            File.WriteAllBytes(trackImage, png);
            string notAnImage = Path.Combine(root, "secrets.txt");
            File.WriteAllText(notAnImage, "not served");
            var control = new OverviewControl(new Dictionary<string, string>
            {
                ["track"] = trackImage,
                ["text"] = notAnImage,
            });
            int port = ReserveLoopbackPort();
            await using var server = new RaceControlWebServer(new RaceControlWebServerOptions(true, "127.0.0.1", port), paths, control);
            await server.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(server.BrowserUrl) };

            using var overview = JsonDocument.Parse(await client.GetStringAsync("api/v1/overview"));
            var image = await client.GetAsync("api/v1/media?key=track");
            var text = await client.GetAsync("api/v1/media?key=text");
            var unknown = await client.GetAsync("api/v1/media?key=" + Uri.EscapeDataString(trackImage));
            string index = await client.GetStringAsync("/");

            Assert.Multiple(async () =>
            {
                var data = overview.RootElement.GetProperty("overview");
                Assert.That(data.GetProperty("trackImageKey").GetString(), Is.EqualTo("track"));
                Assert.That(data.GetProperty("teams")[0].GetProperty("name").GetString(), Is.EqualTo("Night Owls"));
                Assert.That(data.GetProperty("entries")[0].GetProperty("isLive").GetBoolean(), Is.True);
                Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(image.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
                Assert.That(await image.Content.ReadAsByteArrayAsync(), Is.EqualTo(png));
                Assert.That(text.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "only image files are served");
                Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "a file path is not a key");
                Assert.That(index, Does.Contain("id=\"overview-track-image\"").And.Contain("id=\"overview-entries\""));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class OverviewControl(IReadOnlyDictionary<string, string> media) : IRaceControlWebControl
    {
        public WebOverview? GetOverview() => new("Sunday Endurance", "Racing", "Vallelunga", "Club", "Italy", "track", null,
            [new WebOverviewCar("BMW M3 E30", "car:bmw_m3_e30:red", 2)],
            [new WebOverviewTeam("Night Owls", ["Alice", "Bob"], 1)],
            [new WebOverviewEntry(1, "Alice", "Night Owls", "BMW M3 E30", "car:bmw_m3_e30:red", true, "Reserved")]);

        public string? GetMediaPath(string key) => media.GetValueOrDefault(key);

        public RaceControlWebControlState GetState() => new(
            "Sunday Endurance", "Server", "RACING", "RACE", "vallelunga", "club", "",
            "OFFLINE", "Ready", false, true, false, false, false, false, false);

        public Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(RaceControlWebActionResult.Success("ok"));

        public Task<RaceControlWebActionResult> SetEnvironmentAsync(RaceControlWebEnvironmentRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(RaceControlWebActionResult.Success("ok"));
    }
}
