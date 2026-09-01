using System.Text;
using System.IO.Compression;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Staging;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class FpsClientPackAssetsTests
{
    [Test]
    public void RifleModelsAreEmbeddedValidKn5Assets()
    {
        byte[] viewmodel = FpsClientPackAssets.GetRifleViewmodel();
        byte[] worldModel = FpsClientPackAssets.GetRifleWorldModel();

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(viewmodel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(viewmodel.Length, Is.GreaterThan(50_000));
            Assert.That(Encoding.ASCII.GetString(worldModel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(worldModel.Length, Is.GreaterThan(30_000));
            Assert.That(FpsClientPackAssets.Sha256(viewmodel), Has.Length.EqualTo(64));
            Assert.That(FpsClientPackAssets.RifleViewmodelPath, Does.EndWith(".kn5"));
            Assert.That(FpsClientPackAssets.RifleWorldModelPath, Does.EndWith(".kn5"));
        });
    }

    [Test]
    public void GeneratedDiffuseTexturesAreEmbeddedPngAssets()
    {
        byte[] rifle = FpsClientPackAssets.GetRifleDiffuse();
        byte[] operatorTexture = FpsClientPackAssets.GetOperatorSkin();
        byte[] pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

        Assert.Multiple(() =>
        {
            Assert.That(rifle.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(operatorTexture.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(rifle, Has.Length.GreaterThan(100_000));
            Assert.That(operatorTexture, Has.Length.GreaterThan(100_000));
            Assert.That(FpsClientPackAssets.RifleDiffusePath, Does.EndWith(".png"));
            Assert.That(FpsClientPackAssets.OperatorSkinPath,
                Does.EndWith("asrc_operator_skin.png"));
        });
    }

    [Test]
    public void RifleWaveIsAPlayableMonoPcmAsset()
    {
        byte[] wave = FpsClientPackAssets.CreateRifleWave();
        using var reader = new BinaryReader(new MemoryStream(wave), Encoding.ASCII);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("RIFF"));
            reader.BaseStream.Position = 8;
            Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("WAVE"));
            reader.BaseStream.Position = 20;
            Assert.That(reader.ReadInt16(), Is.EqualTo(1));
            Assert.That(reader.ReadInt16(), Is.EqualTo(1));
            Assert.That(reader.ReadInt32(), Is.EqualTo(44_100));
            Assert.That(wave.Length, Is.GreaterThan(19_000));
        });
    }

    [Test]
    public void HudAppAssetsAreEmbeddedAndUseTheVersionedExclusiveBridge()
    {
        string manifest = Encoding.UTF8.GetString(FpsClientPackAssets.GetHudManifest());
        string script = Encoding.UTF8.GetString(FpsClientPackAssets.GetHudScript());
        int topLevelLocalCount = script.Split('\n')
            .Count(line => line.StartsWith("local ", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(FpsClientPackAssets.HudManifestPath,
                Is.EqualTo("apps/lua/asrc_fps_hud/manifest.ini"));
            Assert.That(FpsClientPackAssets.HudScriptPath,
                Is.EqualTo("apps/lua/asrc_fps_hud/asrc_fps_hud.lua"));
            Assert.That(manifest, Does.Contain("NAME = ASRC FPS HUD"));
            Assert.That(manifest, Does.Contain("LAZY = NONE"));
            Assert.That(manifest, Does.Contain("IN_GAME = appOverlay"));
            Assert.That(script, Does.Contain("ac.StructItem.key('asrc.fps.hud.v3')"));
            Assert.That(script, Does.Contain("adsActive = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain("if bridge.adsActive == 0 then"));
            Assert.That(script, Does.Contain("awardPopupTexts"));
            Assert.That(script, Does.Contain("actorScores"));
            Assert.That(script, Does.Contain("actorCapacity = 32"));
            Assert.That(script, Does.Contain("ui.onExclusiveHUD(exclusiveHud, true)"));
            Assert.That(script, Does.Contain("mode ~= 'game'"));
            Assert.That(script, Does.Contain("age >= -0.1 and age <= 0.5"));
            Assert.That(script, Does.Contain("COMBAT RADAR  40 m"));
            Assert.That(script, Does.Contain("bridge.radarFlags[index]"));
            Assert.That(script, Does.Contain("pcall(ffi.string, value)"));
            Assert.That(script, Does.Contain("local right = -(offset.x * rightX + offset.z * rightZ)"));
            Assert.That(script, Does.Not.Contain("tostring(bridge.actorNames[index])"));
            Assert.That(script, Does.Not.Contain("math.lerpAngle"));
            Assert.That(topLevelLocalCount, Is.LessThanOrEqualTo(190));
            Assert.That(FpsClientPackAssets.Sha256(Encoding.UTF8.GetBytes(script)),
                Has.Length.EqualTo(64));
        });
    }

    [Test]
    public async Task ClientPackV15ContainsBothThemesAndOnlyProjectOwnedPayloadPaths()
    {
        await using var stream = new MemoryStream();
        await FpsClientPackBuilder.WriteAsync(stream, "asrc_fps_carrier");
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(FpsClientPackBuilder.ClientPackVersion, Is.EqualTo(15));
            Assert.That(FpsClientPackBuilder.BridgeProtocol, Is.EqualTo(3));
            Assert.That(FpsClientPackBuilder.DefaultFileName,
                Is.EqualTo("asrc-fps-compatibility-client-v15.zip"));
            Assert.That(entries.Keys, Does.Contain("asrc-fps-client.json"));
            Assert.That(entries.Keys, Does.Contain("README.txt"));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.HudManifestPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.HudScriptPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.RifleViewmodelPath));
            Assert.That(entries.Keys, Does.Contain("extension/audio/asrc_fps/rifle.wav"));
            Assert.That(entries.Keys, Does.Contain(
                $"{FpsClientPackAssets.ModernAssetDirectory}asrc_modern_operator_carbine.kn5"));
            Assert.That(entries.Keys, Does.Contain(
                $"{FpsClientPackAssets.ModernAssetDirectory}asrc_modern_carbine_viewmodel.kn5"));
            Assert.That(entries.Keys, Does.Contain(
                $"{FpsClientPackAssets.ModernAssetDirectory}asrc_modern_carbine_pickup.kn5"));
            Assert.That(entries.Keys.Count(path => path.StartsWith(
                FpsClientPackAssets.ModernAssetDirectory, StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(30));
        });

        foreach (string path in entries.Keys.Where(path => path is not "asrc-fps-client.json"
                                                           and not "README.txt"))
            Assert.DoesNotThrow(() => FpsClientPackBuilder.ValidateProjectOwnedPath(path), path);

        byte[] manifestBytes = ReadEntry(entries["asrc-fps-client.json"]);
        using JsonDocument document = JsonDocument.Parse(manifestBytes);
        JsonElement root = document.RootElement;
        JsonElement hud = root.GetProperty("hud");
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("clientPackVersion").GetInt32(), Is.EqualTo(15));
            Assert.That(root.GetProperty("carrierCar").GetString(), Is.EqualTo("asrc_fps_carrier"));
            Assert.That(root.GetProperty("visualThemes").GetProperty("defaultTheme").GetString(),
                Is.EqualTo("Blocks"));
            Assert.That(root.GetProperty("visualThemes").GetProperty("available")
                .EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(new[] { "Blocks", "Modern" }));
            Assert.That(hud.GetProperty("bridge").GetString(), Is.EqualTo("asrc.fps.hud.v3"));
            Assert.That(hud.GetProperty("bridgeProtocol").GetInt32(), Is.EqualTo(3));
            Assert.That(hud.GetProperty("onlineFallback").GetBoolean(), Is.True);
            Assert.That(hud.GetProperty("manifestSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.HudManifestPath]))));
            Assert.That(hud.GetProperty("scriptSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.HudScriptPath]))));
        });
    }

    [Test]
    public void ModernAssets_AreCompleteAndWithinProjectOwnedDirectory()
    {
        IReadOnlyList<(string Path, byte[] Data)> assets = FpsClientPackAssets.GetModernAssets();
        byte[] manifestData = assets.Single(asset => asset.Path.EndsWith(
            "asrc-modern-assets.json", StringComparison.Ordinal)).Data;
        using JsonDocument manifest = JsonDocument.Parse(manifestData);
        JsonElement root = manifest.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(assets, Has.Count.GreaterThanOrEqualTo(30));
            Assert.That(assets.Select(asset => asset.Path), Has.Some.EndsWith(
                "asrc_modern_operator_carbine.kn5"));
            Assert.That(assets.Select(asset => asset.Path), Has.Some.EndsWith(
                "asrc_modern_carbine_viewmodel.kn5"));
            Assert.That(assets.Select(asset => asset.Path), Has.Some.EndsWith(
                "asrc_modern_carbine_pickup.kn5"));
            Assert.That(assets.Count(asset => asset.Path.EndsWith(".ksanim",
                StringComparison.OrdinalIgnoreCase)), Is.EqualTo(26));
            foreach ((string path, byte[] data) in assets)
            {
                Assert.That(path, Does.StartWith(FpsClientPackAssets.ModernAssetDirectory));
                Assert.That(data, Has.Length.GreaterThan(32), path);
                Assert.DoesNotThrow(() => FpsClientPackBuilder.ValidateProjectOwnedPath(path));
            }
            Assert.That(root.GetProperty("redistributionRightsConfirmedByUser").GetBoolean(),
                Is.True);
            Assert.That(root.GetProperty("sources").GetProperty("m4a1Used").GetBoolean(),
                Is.False);
            Assert.That(root.GetProperty("validation").GetProperty("status").GetString(),
                Is.EqualTo("passed"));
            Assert.That(root.GetProperty("validation").GetProperty("stancePosesValidated")
                .GetBoolean(), Is.True);
            Assert.That(root.GetProperty("validation").GetProperty("deathCollapseValidated")
                .GetBoolean(), Is.True);
            Assert.That(root.GetProperty("validation").GetProperty("viewmodelSkinnedMeshes").GetInt32(),
                Is.GreaterThanOrEqualTo(3));
            Assert.That(root.GetProperty("validation").GetProperty("viewmodelWeaponSkinnedMeshes").GetInt32(),
                Is.EqualTo(1));
            Assert.That(root.GetProperty("validation").GetProperty("viewmodelOpticSkinnedMeshes").GetInt32(),
                Is.EqualTo(1));
            Assert.That(root.GetProperty("validation").GetProperty("uniqueNodeNames").GetBoolean(),
                Is.True);
            Assert.That(root.GetProperty("operator").GetProperty("triangles").GetInt32(),
                Is.LessThanOrEqualTo(40_000));
            Assert.That(root.GetProperty("operator").GetProperty("materials").GetInt32(),
                Is.LessThanOrEqualTo(4));
            Assert.That(root.GetProperty("viewmodel").GetProperty("triangles").GetInt32(),
                Is.LessThanOrEqualTo(30_000));
            Assert.That(root.GetProperty("viewmodel").GetProperty("materials").GetInt32(),
                Is.LessThanOrEqualTo(3));
            Assert.That(root.GetProperty("viewmodel").GetProperty(
                "redDotCoreDiameterPixels").GetInt32(), Is.EqualTo(14));
            Assert.That(root.GetProperty("viewmodel").GetProperty(
                "redDotTextureSizePixels").GetInt32(), Is.EqualTo(512));
        });
    }

    [TestCase("../apps/lua/asrc_fps_hud/escape.lua")]
    [TestCase("apps/lua/other_app/script.lua")]
    [TestCase("content/cars/ks_abarth500/data.acd")]
    [TestCase("/apps/lua/asrc_fps_hud/script.lua")]
    public void ClientPackRejectsPathsOutsideProjectOwnedFolders(string path)
    {
        Assert.Throws<InvalidDataException>(() => FpsClientPackBuilder.ValidateProjectOwnedPath(path));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
