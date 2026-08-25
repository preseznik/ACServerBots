using AssettoServer.Server.Configuration.Kunos;

namespace AssettoServer.Tests;

public sealed class RacecraftConfigurationTests
{
    [Test]
    public void EntryList_ParsesPerSlotRacecraftOverridesAndAutomaticSentinel()
    {
        string root = Path.Combine(Path.GetTempPath(), "assettoserver-racecraft-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "entry_list.ini");
        try
        {
            File.WriteAllText(path, """
            [CAR_0]
            MODEL=car_one
            AI=auto
            AI_DIFFICULTY=0.91
            AI_AGGRESSION=0.73

            [CAR_1]
            MODEL=car_two
            AI=fixed
            AI_DIFFICULTY=-1
            AI_AGGRESSION=-1

            [CAR_2]
            MODEL=legacy_car
            AI=auto
            """);

            var entries = EntryList.FromFile(path).Cars;

            Assert.Multiple(() =>
            {
                Assert.That(entries[0].AiDifficulty, Is.EqualTo(0.91f).Within(1e-6f));
                Assert.That(entries[0].AiAggression, Is.EqualTo(0.73f).Within(1e-6f));
                Assert.That(entries[1].AiDifficulty, Is.EqualTo(-1));
                Assert.That(entries[1].AiAggression, Is.EqualTo(-1));
                Assert.That(entries[2].AiDifficulty, Is.EqualTo(-1));
                Assert.That(entries[2].AiAggression, Is.EqualTo(-1));
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
