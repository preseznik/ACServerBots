using System.Text;
using System.IO.Compression;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Staging;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class FpsClientPackAssetsTests
{
    [Test]
    public void WeaponAndGrenadeModelsAreEmbeddedValidAssets()
    {
        byte[] viewmodel = FpsClientPackAssets.GetRifleViewmodel();
        byte[] worldModel = FpsClientPackAssets.GetRifleWorldModel();
        byte[] compactSmgViewmodel = FpsClientPackAssets.GetCompactSmgViewmodel();
        byte[] compactSmgWorldModel = FpsClientPackAssets.GetCompactSmgWorldModel();
        IReadOnlyList<(string Path, byte[] Data)> compactSmgAnimations =
            FpsClientPackAssets.GetCompactSmgAnimations();
        byte[] desertEagleViewmodel = FpsClientPackAssets.GetDesertEagleViewmodel();
        byte[] desertEagleWorldModel = FpsClientPackAssets.GetDesertEagleWorldModel();
        IReadOnlyList<(string Path, byte[] Data)> desertEagleAnimations =
            FpsClientPackAssets.GetDesertEagleAnimations();
        byte[] colt1911Viewmodel = FpsClientPackAssets.GetColt1911Viewmodel();
        byte[] colt1911WorldModel = FpsClientPackAssets.GetColt1911WorldModel();
        IReadOnlyList<(string Path, byte[] Data)> colt1911Animations =
            FpsClientPackAssets.GetColt1911Animations();
        byte[] fragViewmodel = FpsClientPackAssets.GetFragGrenadeViewmodel();
        byte[] fragWorld = FpsClientPackAssets.GetFragGrenadeWorldModel();
        byte[] fragThrow = FpsClientPackAssets.GetFragGrenadeThrow();
        byte[] stickyViewmodel = FpsClientPackAssets.GetStickyGrenadeViewmodel();
        byte[] stickyWorld = FpsClientPackAssets.GetStickyGrenadeWorldModel();
        byte[] stickyThrow = FpsClientPackAssets.GetStickyGrenadeThrow();
        IReadOnlyList<(string Path, byte[] Data)> modernAssets =
            FpsClientPackAssets.GetModernAssets();

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(viewmodel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(viewmodel.Length, Is.GreaterThan(50_000));
            Assert.That(Encoding.ASCII.GetString(worldModel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(worldModel.Length, Is.GreaterThan(30_000));
            Assert.That(FpsClientPackAssets.Sha256(viewmodel), Has.Length.EqualTo(64));
            Assert.That(FpsClientPackAssets.RifleViewmodelPath, Does.EndWith(".kn5"));
            Assert.That(FpsClientPackAssets.RifleWorldModelPath, Does.EndWith(".kn5"));
            Assert.That(Encoding.ASCII.GetString(compactSmgViewmodel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(compactSmgViewmodel, Has.Length.GreaterThan(30_000_000));
            Assert.That(Encoding.ASCII.GetString(compactSmgWorldModel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(compactSmgWorldModel, Has.Length.GreaterThan(3_000_000));
            Assert.That(compactSmgWorldModel.SequenceEqual(worldModel), Is.False);
            Assert.That(compactSmgAnimations, Has.Count.EqualTo(6));
            Assert.That(compactSmgAnimations.All(asset =>
                BitConverter.ToUInt32(asset.Data, 0) == 2), Is.True);
            Assert.That(compactSmgAnimations.Select(asset => asset.Path),
                Has.Some.EndsWith("asrc_compact_smg_reload_empty.ksanim"));
            Assert.That(Encoding.UTF8.GetString(FpsClientPackAssets.GetCompactSmgAttribution()),
                Does.Contain("Rotuma"));
            Assert.That(Encoding.ASCII.GetString(desertEagleViewmodel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(desertEagleViewmodel, Has.Length.GreaterThan(24_000_000));
            Assert.That(Encoding.ASCII.GetString(desertEagleWorldModel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(desertEagleWorldModel, Has.Length.GreaterThan(6_000_000));
            Assert.That(desertEagleWorldModel.SequenceEqual(worldModel), Is.False);
            Assert.That(desertEagleAnimations, Has.Count.EqualTo(5));
            Assert.That(desertEagleAnimations.All(asset =>
                BitConverter.ToUInt32(asset.Data, 0) == 2), Is.True);
            foreach ((string path, byte[] data) in desertEagleAnimations)
            {
                string clip = Path.GetFileName(path)
                    .Replace("asrc_desert_eagle_", string.Empty, StringComparison.Ordinal);
                byte[]? carbine = modernAssets.FirstOrDefault(asset => asset.Path.EndsWith(
                    $"asrc_modern_carbine_{clip}", StringComparison.Ordinal)).Data;
                if (carbine is not null)
                    Assert.That(data.SequenceEqual(carbine), Is.False,
                        $"Desert Eagle {clip} must use its pistol-specific animation");
            }
            Assert.That(desertEagleAnimations.Select(asset => asset.Path),
                Has.Some.EndsWith("asrc_desert_eagle_reload.ksanim"));
            Assert.That(Encoding.UTF8.GetString(FpsClientPackAssets.GetDesertEagleAttribution()),
                Does.Contain("CC BY 4.0"));
            Assert.That(Encoding.ASCII.GetString(colt1911Viewmodel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(colt1911Viewmodel, Has.Length.GreaterThan(10_000_000));
            Assert.That(Encoding.ASCII.GetString(colt1911WorldModel, 0, 6),
                Is.EqualTo("sc6969"));
            Assert.That(colt1911WorldModel, Has.Length.GreaterThan(1_000_000));
            Assert.That(colt1911WorldModel.SequenceEqual(worldModel), Is.False);
            Assert.That(colt1911WorldModel.SequenceEqual(desertEagleWorldModel), Is.False);
            Assert.That(colt1911Animations, Has.Count.EqualTo(5));
            Assert.That(colt1911Animations.All(asset =>
                BitConverter.ToUInt32(asset.Data, 0) == 2), Is.True);
            Assert.That(colt1911Animations.Select(asset => asset.Path),
                Has.Some.EndsWith("asrc_colt_1911_reload.ksanim"));
            Assert.That(Encoding.UTF8.GetString(FpsClientPackAssets.GetColt1911Attribution()),
                Does.Contain("DanaeH"));
            Assert.That(Encoding.ASCII.GetString(fragViewmodel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(Encoding.ASCII.GetString(fragWorld, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(BitConverter.ToUInt32(fragThrow, 0), Is.EqualTo(2));
            Assert.That(Encoding.ASCII.GetString(stickyViewmodel, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(Encoding.ASCII.GetString(stickyWorld, 0, 6), Is.EqualTo("sc6969"));
            Assert.That(BitConverter.ToUInt32(stickyThrow, 0), Is.EqualTo(2));
            Assert.That(fragWorld.SequenceEqual(worldModel), Is.False);
            Assert.That(stickyWorld.SequenceEqual(worldModel), Is.False);
            Assert.That(Encoding.UTF8.GetString(FpsClientPackAssets.GetFragGrenadeAttribution()),
                Does.Contain("Tiago Lopes"));
            Assert.That(Encoding.UTF8.GetString(FpsClientPackAssets.GetStickyGrenadeAttribution()),
                Does.Contain("Simplix"));
        });
    }

    [Test]
    public void GeneratedDiffuseTexturesAreEmbeddedPngAssets()
    {
        byte[] rifle = FpsClientPackAssets.GetRifleDiffuse();
        byte[] operatorTexture = FpsClientPackAssets.GetOperatorSkin();
        byte[] hudWeapon = FpsClientPackAssets.GetHudWeaponImage();
        byte[] pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

        Assert.Multiple(() =>
        {
            Assert.That(rifle.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(operatorTexture.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(hudWeapon.AsSpan(0, 8).SequenceEqual(pngMagic), Is.True);
            Assert.That(rifle, Has.Length.GreaterThan(100_000));
            Assert.That(operatorTexture, Has.Length.GreaterThan(100_000));
            Assert.That(hudWeapon, Has.Length.GreaterThan(10_000));
            Assert.That(FpsClientPackAssets.RifleDiffusePath, Does.EndWith(".png"));
            Assert.That(FpsClientPackAssets.OperatorSkinPath,
                Does.EndWith("asrc_operator_skin.png"));
        });
    }

    [Test]
    public void MixedAudioCatalogIsCompleteManifestedAndPlayable()
    {
        IReadOnlyList<(string Path, byte[] Data)> assets = FpsClientPackAssets.GetAudioAssets();
        var waves = assets.Where(asset => asset.Path.EndsWith(".wav",
            StringComparison.OrdinalIgnoreCase)).ToArray();
        byte[] manifestBytes = assets.Single(asset =>
            asset.Path == FpsClientPackAssets.AudioManifestPath).Data;
        string notice = Encoding.UTF8.GetString(assets.Single(asset =>
            asset.Path == FpsClientPackAssets.AudioNoticePath).Data);
        using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
        JsonElement[] clips = manifest.RootElement.GetProperty("clips").EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(assets, Has.Count.EqualTo(56));
            Assert.That(waves, Has.Length.EqualTo(54));
            Assert.That(clips, Has.Length.EqualTo(54));
            Assert.That(manifest.RootElement.GetProperty("catalogVersion").GetInt32(),
                Is.EqualTo(1));
            Assert.That(manifest.RootElement.GetProperty("generator")
                .GetProperty("commercialLicenseConfirmedByUser").GetBoolean(), Is.True);
            Assert.That(notice, Does.Contain("ElevenLabs"));
            Assert.That(notice, Does.Contain("paid plan"));
            Assert.That(notice, Does.Contain("GboxMikeFozzy"));
            Assert.That(notice, Does.Contain("Ben Jaszczak"));
            Assert.That(notice, Does.Contain("CC0"));
            Assert.That(clips.Count(clip => clip.TryGetProperty("source", out _)), Is.EqualTo(7));
            Assert.That(clips.Select(clip => clip.GetProperty("id").GetString()), Is.Unique);
            Assert.That(clips.Select(clip => clip.GetProperty("path").GetString()), Is.Unique);
            foreach ((string path, byte[] data) in waves)
            {
                Assert.That(Encoding.ASCII.GetString(data, 0, 4), Is.EqualTo("RIFF"), path);
                Assert.That(Encoding.ASCII.GetString(data, 8, 4), Is.EqualTo("WAVE"), path);
                Assert.That(BitConverter.ToInt16(data, 20), Is.EqualTo(1), path);
                Assert.That(BitConverter.ToInt16(data, 22), Is.EqualTo(1), path);
                Assert.That(BitConverter.ToInt32(data, 24), Is.EqualTo(44_100), path);
                Assert.That(BitConverter.ToInt16(data, 34), Is.EqualTo(16), path);
                JsonElement clip = clips.Single(value =>
                    value.GetProperty("path").GetString() == path);
                bool footstep = path.Contains("/footstep_boot_", StringComparison.Ordinal);
                bool recorded = footstep || path.EndsWith("/fire_assault_rifle_02.wav", StringComparison.Ordinal);
                if (recorded)
                {
                    JsonElement source = clip.GetProperty("source");
                    Assert.That(source.GetProperty("author").GetString(), Is.EqualTo(footstep
                        ? "GboxMikeFozzy" : "Ben Jaszczak, Brian Nelson, Kevin Heras and Matthew Nanney"));
                    Assert.That(source.GetProperty("license").GetString(), Is.EqualTo("CC0-1.0"));
                    Assert.That(source.GetProperty("url").GetString(),
                        Is.EqualTo(footstep ? "https://opengameart.org/content/footsteps-0"
                            : "https://opengameart.org/content/the-free-firearm-sound-library"));
                    Assert.That(source.GetProperty("sha256").GetString(), Has.Length.EqualTo(64));
                    Assert.That(source.GetProperty("processing").GetString(), Is.Not.Empty);
                    Assert.That(clip.TryGetProperty("model", out _), Is.False);
                    Assert.That(DateTimeOffset.TryParse(
                        clip.GetProperty("importedAtUtc").GetString(), out _), Is.True, path);
                    if (!footstep)
                    {
                        Assert.That(clip.GetProperty("sha256").GetString(), Is.EqualTo(
                            "93b6d1400a4e0c6d35c31f859717e8af2b27e07c10285e20a8e7eeb67369933f"));
                        Assert.That(source.GetProperty("file").GetString(),
                            Is.EqualTo("Prepared SFX Library/AR-15/D_32P.wav"));
                        Assert.That(source.GetProperty("startSeconds").GetDouble(), Is.EqualTo(0.701));
                        Assert.That(clip.GetProperty("durationSeconds").GetDouble(), Is.EqualTo(0.5));
                    }
                }
                else
                {
                    Assert.That(clip.GetProperty("prompt").GetString(), Is.Not.Empty, path);
                    Assert.That(clip.GetProperty("model").GetString(),
                        Is.EqualTo("eleven_text_to_sound_v2"), path);
                    Assert.That(DateTimeOffset.TryParse(
                        clip.GetProperty("generatedAtUtc").GetString(), out _), Is.True, path);
                }
                Assert.That(clip.GetProperty("sha256").GetString(),
                    Is.EqualTo(FpsClientPackAssets.Sha256(data)), path);
                Assert.That(clip.GetProperty("peakDb").GetDouble(), Is.EqualTo(footstep ? -6 : -1).Within(0.05),
                    path);
            }
        });
    }

    [Test]
    public void RejectedDesertEagleVariantReusesExistingShotAndProvenance()
    {
        var assets = FpsClientPackAssets.GetAudioAssets();
        using JsonDocument manifest = JsonDocument.Parse(assets.Single(asset =>
            asset.Path == FpsClientPackAssets.AudioManifestPath).Data);
        var clips = manifest.RootElement.GetProperty("clips").EnumerateArray().ToArray();
        JsonElement source = clips.Single(clip => clip.GetProperty("id").GetString() == "fire_desert_eagle_01");
        JsonElement copy = clips.Single(clip => clip.GetProperty("id").GetString() == "fire_desert_eagle_03");
        Assert.Multiple(() =>
        {
            Assert.That(copy.GetProperty("copyOf").GetString(), Is.EqualTo("fire_desert_eagle_01"));
            foreach (string field in new[] { "prompt", "model", "generatedAtUtc", "durationSeconds", "sha256" })
                Assert.That(copy.GetProperty(field).ToString(), Is.EqualTo(source.GetProperty(field).ToString()), field);
            Assert.That(assets.Single(asset => asset.Path == copy.GetProperty("path").GetString()).Data,
                Is.EqualTo(assets.Single(asset => asset.Path == source.GetProperty("path").GetString()).Data));
            Assert.That(copy.GetProperty("sha256").GetString(), Is.Not.EqualTo(
                "d9334a4aee43186f1e9c022e477ba67048c990998ab7569833d925c4c0cfb4f4"));
        });
    }

    [Test]
    public void HudAppAssetsUseTheVersionedBridgeAndOwnFileAudioPlayback()
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
            Assert.That(manifest, Does.Contain("VERSION = 1.3.0"));
            Assert.That(manifest, Does.Contain("LAZY = NONE"));
            Assert.That(manifest, Does.Contain("IN_GAME = appOverlay"));
            Assert.That(script, Does.Contain("ac.StructItem.key('asrc.fps.hud.v6')"));
            Assert.That(script, Does.Contain("outOfBoundsRemaining = ac.StructItem.float()"));
            Assert.That(script, Does.Contain("RETURN TO PLAYABLE AREA"));
            Assert.That(script, Does.Contain("ui.drawRectFilled(vec2(), size, rgbm(0.12, 0, 0, 0.34))"));
            Assert.That(script, Does.Contain("ui.dwriteDrawTextClipped('RETURN TO PLAYABLE AREA'"));
            Assert.That(script, Does.Contain("190 * scale"));
            Assert.That(script, Does.Contain("localStamina = ac.StructItem.byte()"));
            Assert.That(script, Does.Contain("STAMINA  %d%%"));
            Assert.That(script, Does.Contain("ui.drawImage(weaponImagePath"));
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
            Assert.That(script, Does.Contain("ac.onSharedEvent('asrc.fps.audio.v1'"));
            Assert.That(script, Does.Contain("io.fileExists(filePath)"));
            Assert.That(script, Does.Contain("ac.AudioEvent.fromFile"));
            Assert.That(script, Does.Contain("use3D = not localSound"));
            Assert.That(script, Does.Contain("useOcclusion = not localSound"));
            Assert.That(script, Does.Contain("function audioPlayer.distanceGain(position, maxDistance)"));
            Assert.That(script, Does.Contain("if distance >= maxDistance then return 0 end"));
            Assert.That(script, Does.Contain("math.clamp(remaining, 0, 1) ^ 2"));
            Assert.That(script, Does.Contain("volume = volume * audioPlayer.distanceGain(position, maxDistance)"));
            Assert.That(script, Does.Contain("if volume <= 0.001 then return end"));
            Assert.That(script, Does.Contain("event.cameraInteriorMultiplier = 1"));
            Assert.That(script, Does.Contain("maxActive = 64"));
            Assert.That(script, Does.Contain("audioPlayer.update(dt)"));
            Assert.That(script, Does.Contain("fileName:match('^[a-z0-9_]+%.wav$')"));
            Assert.That(script, Does.Contain("[ASRC FPS HUD] file-backed audio playback valid:"));
            Assert.That(script, Does.Contain("local right = -(offset.x * rightX + offset.z * rightZ)"));
            Assert.That(script, Does.Not.Contain("tostring(bridge.actorNames[index])"));
            Assert.That(script, Does.Not.Contain("math.lerpAngle"));
            Assert.That(topLevelLocalCount, Is.LessThanOrEqualTo(190));
            Assert.That(FpsClientPackAssets.Sha256(Encoding.UTF8.GetBytes(script)),
                Has.Length.EqualTo(64));
        });
    }

    [Test]
    public async Task ClientPackV38ContainsAudioAnimatedWeaponsGrenadesAndBothThemes()
    {
        await using var stream = new MemoryStream();
        await FpsClientPackBuilder.WriteAsync(stream, "asrc_fps_carrier");
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(FpsClientPackBuilder.ClientPackVersion, Is.EqualTo(38));
            Assert.That(FpsClientPackBuilder.BridgeProtocol, Is.EqualTo(6));
            Assert.That(FpsClientPackBuilder.DefaultFileName,
                Is.EqualTo("asrc-fps-compatibility-client-v38.zip"));
            Assert.That(entries.Keys, Does.Contain("asrc-fps-client.json"));
            Assert.That(entries.Keys, Does.Contain("README.txt"));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.HudManifestPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.HudScriptPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.HudWeaponImagePath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.RifleViewmodelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.CompactSmgViewmodelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.CompactSmgWorldModelPath));
            foreach (string animation in FpsClientPackAssets.CompactSmgAnimationPaths)
                Assert.That(entries.Keys, Does.Contain(animation));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.CompactSmgAttributionPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.DesertEagleWorldModelPath));
            foreach (string animation in FpsClientPackAssets.DesertEagleAnimationPaths)
                Assert.That(entries.Keys, Does.Contain(animation));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.DesertEagleAttributionPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.Colt1911WorldModelPath));
            foreach (string animation in FpsClientPackAssets.Colt1911AnimationPaths)
                Assert.That(entries.Keys, Does.Contain(animation));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.Colt1911AttributionPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.FragGrenadeWorldModelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.FragGrenadeViewmodelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.FragGrenadeThrowPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.FragGrenadeAttributionPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.StickyGrenadeWorldModelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.StickyGrenadeViewmodelPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.StickyGrenadeThrowPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.StickyGrenadeAttributionPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.AudioManifestPath));
            Assert.That(entries.Keys, Does.Contain(FpsClientPackAssets.AudioNoticePath));
            Assert.That(entries.Keys.Count(path => path.StartsWith(
                FpsClientPackAssets.AudioAssetDirectory, StringComparison.Ordinal)
                && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(54));
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
        JsonElement audio = root.GetProperty("audio");
        JsonElement hud = root.GetProperty("hud");
        JsonElement compactSmg = root.GetProperty("loadoutItems").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 2);
        JsonElement desertEagle = root.GetProperty("loadoutItems").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 3);
        JsonElement colt1911 = root.GetProperty("loadoutItems").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 4);
        JsonElement frag = root.GetProperty("loadoutItems").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 16);
        JsonElement sticky = root.GetProperty("loadoutItems").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 17);
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("protocol").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("clientPackVersion").GetInt32(), Is.EqualTo(38));
            Assert.That(root.GetProperty("loadoutItems").GetArrayLength(), Is.EqualTo(5));
            Assert.That(root.GetProperty("carrierCar").GetString(), Is.EqualTo("asrc_fps_carrier"));
            Assert.That(root.GetProperty("visualThemes").GetProperty("defaultTheme").GetString(),
                Is.EqualTo("Blocks"));
            Assert.That(root.GetProperty("visualThemes").GetProperty("available")
                .EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(new[] { "Blocks", "Modern" }));
            Assert.That(audio.GetProperty("catalogVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(audio.GetProperty("playbackOwner").GetString(), Is.EqualTo("ASRC FPS HUD"));
            Assert.That(audio.GetProperty("relay").GetString(), Is.EqualTo("asrc.fps.audio.v1"));
            Assert.That(audio.GetProperty("clipCount").GetInt32(), Is.EqualTo(54));
            Assert.That(audio.GetProperty("clips").GetArrayLength(), Is.EqualTo(54));
            Assert.That(audio.GetProperty("manifestSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.AudioManifestPath]))));
            Assert.That(audio.GetProperty("noticeSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.AudioNoticePath]))));
            foreach (JsonElement clip in audio.GetProperty("clips").EnumerateArray())
            {
                string path = clip.GetProperty("path").GetString()!;
                Assert.That(clip.GetProperty("sha256").GetString(),
                    Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[path]))));
            }
            Assert.That(hud.GetProperty("bridge").GetString(), Is.EqualTo("asrc.fps.hud.v6"));
            Assert.That(hud.GetProperty("bridgeProtocol").GetInt32(), Is.EqualTo(6));
            Assert.That(hud.GetProperty("onlineFallback").GetBoolean(), Is.True);
            Assert.That(hud.GetProperty("manifestSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.HudManifestPath]))));
            Assert.That(hud.GetProperty("scriptSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.HudScriptPath]))));
            Assert.That(hud.GetProperty("weaponImageSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.HudWeaponImagePath]))));
            Assert.That(compactSmg.GetProperty("placeholder").GetBoolean(), Is.False);
            Assert.That(compactSmg.GetProperty("viewmodelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.CompactSmgViewmodelPath]))));
            Assert.That(compactSmg.GetProperty("worldModelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.CompactSmgWorldModelPath]))));
            Assert.That(compactSmg.GetProperty("animations").GetArrayLength(), Is.EqualTo(6));
            foreach (JsonElement animation in compactSmg.GetProperty("animations").EnumerateArray())
            {
                string path = animation.GetProperty("path").GetString()!;
                Assert.That(animation.GetProperty("sha256").GetString(),
                    Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[path]))));
            }
            Assert.That(compactSmg.GetProperty("attributionSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.CompactSmgAttributionPath]))));
            Assert.That(ReadEntry(entries[FpsClientPackAssets.CompactSmgWorldModelPath])
                .SequenceEqual(ReadEntry(entries[FpsClientPackAssets.RifleWorldModelPath])),
                Is.False);
            Assert.That(desertEagle.GetProperty("placeholder").GetBoolean(), Is.False);
            Assert.That(desertEagle.GetProperty("viewmodelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.DesertEagleViewmodelPath]))));
            Assert.That(desertEagle.GetProperty("worldModelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.DesertEagleWorldModelPath]))));
            Assert.That(desertEagle.GetProperty("animations").GetArrayLength(), Is.EqualTo(5));
            foreach (JsonElement animation in desertEagle.GetProperty("animations").EnumerateArray())
            {
                string path = animation.GetProperty("path").GetString()!;
                Assert.That(animation.GetProperty("sha256").GetString(),
                    Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[path]))));
            }
            Assert.That(desertEagle.GetProperty("attributionSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.DesertEagleAttributionPath]))));
            Assert.That(ReadEntry(entries[FpsClientPackAssets.DesertEagleWorldModelPath])
                .SequenceEqual(ReadEntry(entries[FpsClientPackAssets.RifleWorldModelPath])),
                Is.False);
            Assert.That(colt1911.GetProperty("placeholder").GetBoolean(), Is.False);
            Assert.That(colt1911.GetProperty("viewmodelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.Colt1911ViewmodelPath]))));
            Assert.That(colt1911.GetProperty("worldModelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.Colt1911WorldModelPath]))));
            Assert.That(colt1911.GetProperty("animations").GetArrayLength(), Is.EqualTo(5));
            foreach (JsonElement animation in colt1911.GetProperty("animations").EnumerateArray())
            {
                string path = animation.GetProperty("path").GetString()!;
                Assert.That(animation.GetProperty("sha256").GetString(),
                    Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[path]))));
            }
            Assert.That(colt1911.GetProperty("attributionSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(
                    ReadEntry(entries[FpsClientPackAssets.Colt1911AttributionPath]))));
            Assert.That(ReadEntry(entries[FpsClientPackAssets.Colt1911WorldModelPath])
                .SequenceEqual(ReadEntry(entries[FpsClientPackAssets.RifleWorldModelPath])),
                Is.False);
            AssertGrenadeManifest(frag, FpsClientPackAssets.FragGrenadeViewmodelPath,
                FpsClientPackAssets.FragGrenadeWorldModelPath,
                FpsClientPackAssets.FragGrenadeThrowPath,
                FpsClientPackAssets.FragGrenadeAttributionPath);
            AssertGrenadeManifest(sticky, FpsClientPackAssets.StickyGrenadeViewmodelPath,
                FpsClientPackAssets.StickyGrenadeWorldModelPath,
                FpsClientPackAssets.StickyGrenadeThrowPath,
                FpsClientPackAssets.StickyGrenadeAttributionPath);
        });

        void AssertGrenadeManifest(JsonElement item, string viewmodelPath,
            string worldModelPath, string throwPath, string attributionPath)
        {
            Assert.That(item.GetProperty("placeholder").GetBoolean(), Is.False);
            Assert.That(item.GetProperty("viewmodelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[viewmodelPath]))));
            Assert.That(item.GetProperty("worldModelSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[worldModelPath]))));
            Assert.That(item.GetProperty("throwAnimationSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[throwPath]))));
            Assert.That(item.GetProperty("attributionSha256").GetString(),
                Is.EqualTo(FpsClientPackAssets.Sha256(ReadEntry(entries[attributionPath]))));
        }
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
