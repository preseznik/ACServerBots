using System.Text.Json;
using AssettoServer.RaceControl.Core.Runtime;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class RaceResultsReportTests
{
    // The server writes camelCase JSON (JsonSerializerDefaults.Web); keep this in that shape.
    private const string FinishedRaceJson = """
        {
          "schemaVersion": 1,
          "status": "finished",
          "sessionName": "Race",
          "track": "ks_vallelunga",
          "layout": "club_circuit",
          "targetLaps": 20,
          "startedAt": "2026-11-10T20:00:00+00:00",
          "recordedAt": "2026-11-10T20:35:00+00:00",
          "results": [
            { "sessionId": 2, "position": 2, "name": "Carla, \"Fast\"", "drivers": [], "team": "", "model": "bmw_m3_e30",
              "isBot": false, "laps": 20, "totalTimeMilliseconds": 1805250, "bestLapMilliseconds": 89100,
              "finished": true, "dnf": false },
            { "sessionId": 0, "position": 1, "name": "Bob", "drivers": ["Alice", "Bob"], "team": "Team A", "model": "bmw_m3_e30",
              "isBot": false, "laps": 20, "totalTimeMilliseconds": 1800000, "bestLapMilliseconds": 88500,
              "finished": true, "dnf": false },
            { "sessionId": 1, "position": 3, "name": "Bot Rossi", "drivers": [], "team": "", "model": "bmw_m3_e30",
              "isBot": true, "laps": 18, "totalTimeMilliseconds": 1700000, "bestLapMilliseconds": 91000,
              "finished": false, "dnf": false },
            { "sessionId": 3, "position": 4, "name": "Dave", "drivers": [], "team": "", "model": "bmw_m3_e30",
              "isBot": false, "laps": 0, "totalTimeMilliseconds": 0, "bestLapMilliseconds": 999999999,
              "finished": false, "dnf": true }
          ]
        }
        """;

    [Test]
    public void Classify_OrdersRowsAndComputesGapsAndOutcomes()
    {
        var report = Load(FinishedRaceJson);

        Assert.Multiple(() =>
        {
            Assert.That(report.Title, Is.EqualTo("RACE RESULTS"));
            Assert.That(report.Results.Select(row => row.Position), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(report.Results[0].Driver, Is.EqualTo("Alice / Bob"));
            Assert.That(report.Results[0].Time, Is.EqualTo("30:00.000"));
            Assert.That(report.Results[0].Gap, Is.EqualTo("—"));
            Assert.That(report.Results[1].Gap, Is.EqualTo("+0:05.250"));
            Assert.That(report.Results[2].Gap, Is.EqualTo("+2 laps"));
            Assert.That(report.Results[2].Outcome, Is.EqualTo("RUNNING"));
            Assert.That(report.Results[3].Outcome, Is.EqualTo("DNF"));
            Assert.That(report.Results[3].BestLap, Is.EqualTo("—"));
            Assert.That(report.Overview, Does.StartWith("ks_vallelunga / club_circuit  •  4 cars  •  20 laps"));
        });
    }

    [Test]
    public void ToCsv_EscapesNamesAndListsSharedDrivers()
    {
        var lines = Load(FinishedRaceJson).ToCsv().TrimEnd().Split(Environment.NewLine);

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("Position,Driver,Drivers,Team,Car,Bot,Laps,Total time,Gap,Best lap,Result"));
            Assert.That(lines[1], Is.EqualTo("1,Bob,Alice / Bob,Team A,bmw_m3_e30,no,20,30:00.000,—,1:28.500,FINISHED"));
            Assert.That(lines[2], Is.EqualTo("2,\"Carla, \"\"Fast\"\"\",,,bmw_m3_e30,no,20,30:05.250,+0:05.250,1:29.100,FINISHED"));
            Assert.That(lines[3], Does.EndWith(",yes,18,28:20.000,+2 laps,1:31.000,RUNNING"));
            Assert.That(lines, Has.Length.EqualTo(5));
        });
    }

    [Test]
    public void ToText_IsReadableForChat()
    {
        var text = Load(FinishedRaceJson).ToText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("RACE RESULTS – ks_vallelunga / club_circuit"));
            Assert.That(text, Does.Contain("1. Alice / Bob (bmw_m3_e30) – 20 laps – 30:00.000 – best 1:28.500"));
            Assert.That(text, Does.Contain("3. Bot Rossi (bmw_m3_e30) – 18 laps (+2 laps) – best 1:31.000 – RUNNING"));
            Assert.That(text, Does.Contain("4. Dave (bmw_m3_e30) – 0 laps – DNF"));
        });
    }

    [Test]
    public void UnfinishedStatuses_SayResultsAreIncomplete()
    {
        var report = Load(FinishedRaceJson.Replace("\"status\": \"finished\"", "\"status\": \"server_stopped\""));

        Assert.Multiple(() =>
        {
            Assert.That(report.Title, Does.StartWith("SERVER STOPPED"));
            Assert.That(report.Results[2].Outcome, Is.EqualTo("NOT FINISHED"));
        });
    }

    [Test]
    public void TryReadLatestRaceResults_ReturnsNewestFileFromThisRun()
    {
        string root = Path.Combine(Path.GetTempPath(), $"race-results-{Guid.NewGuid():N}");
        try
        {
            var client = new LiveRaceControlClient(root);
            Directory.CreateDirectory(client.ResultsDirectory);
            string older = Path.Combine(client.ResultsDirectory, "race-20261110-190000-session2.json");
            string newer = Path.Combine(client.ResultsDirectory, "race-20261110-200000-session2.json");
            File.WriteAllText(older, FinishedRaceJson.Replace("\"Race\"", "\"Old race\""));
            File.WriteAllText(newer, FinishedRaceJson);
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 11, 10, 19, 30, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 11, 10, 20, 35, 0, DateTimeKind.Utc));

            var latest = client.TryReadLatestRaceResults(new DateTime(2026, 11, 10, 19, 0, 0, DateTimeKind.Utc));
            var afterRestart = client.TryReadLatestRaceResults(new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc));

            Assert.Multiple(() =>
            {
                Assert.That(latest?.Path, Is.EqualTo(newer));
                Assert.That(latest?.Report.SessionName, Is.EqualTo("Race"));
                Assert.That(latest?.Report.Results[1].Gap, Is.EqualTo("+0:05.250"), "reader classifies the report");
                Assert.That(afterRestart, Is.Null, "results from before the server started are not new");
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static RaceResultsReport Load(string json)
        => JsonSerializer.Deserialize<RaceResultsReport>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })!.Classify();
}
