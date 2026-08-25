using AssettoServer.RaceControl.Core.Content;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Tests;

internal sealed class TestContentFactory : IDisposable
{
    public TestContentFactory()
    {
        Root = Path.Combine(Path.GetTempPath(), "race-control-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string AcRoot => Path.Combine(Root, "assettocorsa");
    public string PayloadRoot => Path.Combine(Root, "payload");
    public string DataRoot => Path.Combine(Root, "data");

    public void CreateInstallation(int pitBoxes = 4, bool fastLane = true, params string[] carIds)
    {
        if (carIds.Length == 0)
        {
            carIds = ["car_one"];
        }

        foreach (var (carId, index) in carIds.Select((id, index) => (id, index)))
        {
            var carRoot = Path.Combine(AcRoot, "content", "cars", carId);
            Directory.CreateDirectory(Path.Combine(carRoot, "ui"));
            Directory.CreateDirectory(Path.Combine(carRoot, "skins", "skin_00"));
            File.WriteAllText(Path.Combine(carRoot, "ui", "ui_car.json"), $$"""
                {
                  "name": "Test Car {{index + 1}}",
                  "brand": "Codex",
                  "class": "race",
                  "country": "Italy",
                  "year": {{2000 + index}},
                  "tags": ["test", "race"],
                  "specs": { "bhp": "200 bhp", "weight": "1000 kg", "topspeed": "220 km/h" }
                }
                """);
            File.WriteAllText(Path.Combine(carRoot, "skins", "skin_00", "ui_skin.json"), "{ \"skinname\": \"Red\" }");
            File.WriteAllText(Path.Combine(carRoot, "skins", "skin_00", "preview.jpg"), "preview");
            File.WriteAllText(Path.Combine(carRoot, "data.acd"), "checksum");
            File.WriteAllText(Path.Combine(carRoot, "collider.kn5"), "collider");
        }

        var trackRoot = Path.Combine(AcRoot, "content", "tracks", "test_track");
        Directory.CreateDirectory(Path.Combine(trackRoot, "ui"));
        Directory.CreateDirectory(Path.Combine(trackRoot, "ai"));
        Directory.CreateDirectory(Path.Combine(AcRoot, "content", "weather", "3_clear"));
        File.WriteAllText(Path.Combine(trackRoot, "ui", "ui_track.json"), $$"""
            { "name": "Test Track", "country": "Italy", "city": "Test", "pitboxes": "{{pitBoxes}}" }
            """);
        File.WriteAllText(Path.Combine(trackRoot, "models.ini"), "[MODEL_0]\nFILE=track.kn5\n");
        File.WriteAllText(Path.Combine(trackRoot, "track.kn5"), "track");
        if (fastLane)
        {
            WriteClosedFastLane(Path.Combine(trackRoot, "ai", "fast_lane.ai"));
        }
        File.WriteAllText(Path.Combine(AcRoot, "content", "weather", "3_clear", "weather.ini"), "[LAUNCHER]\nNAME=Clear\n");

        Directory.CreateDirectory(PayloadRoot);
        File.WriteAllText(Path.Combine(PayloadRoot, "AssettoServer.exe"), "fake server");
        File.WriteAllText(Path.Combine(PayloadRoot, "support.dll"), "fake support");
    }

    public AcContentCatalog Scan() => new AcContentScanner().Scan(AcRoot);

    private static void WriteClosedFastLane(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        const int count = 32;
        writer.Write(-1);
        writer.Write(count);
        for (int index = 0; index < count; index++)
        {
            double angle = index * Math.PI * 2 / (count - 1);
            writer.Write((float)(Math.Cos(angle) * 100));
            writer.Write(0f);
            writer.Write((float)(Math.Sin(angle) * 100));
            writer.Write(100f);
            writer.Write(0f);
        }
    }

    public RaceControlPreset CreatePreset(int slots = 2, bool bots = true)
    {
        var preset = RaceControlPreset.CreateDefault(AcRoot, PayloadRoot);
        preset.Name = "Test Event";
        preset.TrackId = "test_track";
        preset.TrackLayoutId = string.Empty;
        preset.Network.BindAddress = "192.168.1.10";
        preset.Bots.Enabled = bots;
        var cars = Scan().Cars;
        preset.Grid = Enumerable.Range(0, slots).Select(index => new GridSlotPreset
        {
            CarId = cars[index % cars.Count].Id,
            SkinId = "skin_00",
            DriverName = $"Bot {index + 1:00}",
            Mode = SlotMode.Auto,
        }).ToList();
        return preset;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
