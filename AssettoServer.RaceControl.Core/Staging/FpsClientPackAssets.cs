using System.Text;

namespace AssettoServer.RaceControl.Core.Staging;

public static class FpsClientPackAssets
{
    private const string ModernResourcePrefix =
        "AssettoServer.RaceControl.Core.Assets.Fps.Modern.";
    private const string AudioResourcePrefix =
        "AssettoServer.RaceControl.Core.Assets.Fps.Audio.";
    public const string ModernAssetDirectory = "content/objects3D/asrc_fps/modern/";
    public const string AudioAssetDirectory = "extension/audio/asrc_fps/";
    public const string AudioManifestPath = AudioAssetDirectory + "audio-manifest.json";
    public const string AudioNoticePath = AudioAssetDirectory + "NOTICE.txt";
    public const string RifleViewmodelPath =
        "content/objects3D/asrc_fps/asrc_assault_rifle_viewmodel.kn5";
    public const string RifleWorldModelPath =
        "content/objects3D/asrc_fps/asrc_assault_rifle_world.kn5";
    public const string RifleDiffusePath =
        "content/objects3D/asrc_fps/asrc_rifle_diffuse.png";
    public const string OperatorSkinPath =
        "content/objects3D/asrc_fps/asrc_operator_skin.png";
    public const string CompactSmgViewmodelPath =
        "content/objects3D/asrc_fps/asrc_compact_smg_viewmodel.kn5";
    public const string CompactSmgWorldModelPath =
        "content/objects3D/asrc_fps/asrc_compact_smg_world.kn5";
    public static readonly string[] CompactSmgAnimationPaths =
    [
        "content/objects3D/asrc_fps/asrc_compact_smg_idle.ksanim",
        "content/objects3D/asrc_fps/asrc_compact_smg_fire.ksanim",
        "content/objects3D/asrc_fps/asrc_compact_smg_reload.ksanim",
        "content/objects3D/asrc_fps/asrc_compact_smg_reload_empty.ksanim",
        "content/objects3D/asrc_fps/asrc_compact_smg_equip.ksanim",
        "content/objects3D/asrc_fps/asrc_compact_smg_sprint.ksanim",
    ];
    public const string CompactSmgAttributionPath =
        "content/objects3D/asrc_fps/attribution/compact-smg.txt";
    public const string DesertEagleViewmodelPath =
        "content/objects3D/asrc_fps/asrc_desert_eagle_viewmodel.kn5";
    public const string DesertEagleWorldModelPath =
        "content/objects3D/asrc_fps/asrc_desert_eagle_world.kn5";
    public static readonly string[] DesertEagleAnimationPaths =
    [
        "content/objects3D/asrc_fps/asrc_desert_eagle_idle.ksanim",
        "content/objects3D/asrc_fps/asrc_desert_eagle_fire.ksanim",
        "content/objects3D/asrc_fps/asrc_desert_eagle_equip.ksanim",
        "content/objects3D/asrc_fps/asrc_desert_eagle_sprint.ksanim",
        "content/objects3D/asrc_fps/asrc_desert_eagle_reload.ksanim",
    ];
    public const string DesertEagleAttributionPath =
        "content/objects3D/asrc_fps/attribution/desert-eagle.txt";
    public const string Colt1911ViewmodelPath =
        "content/objects3D/asrc_fps/asrc_colt_1911_viewmodel.kn5";
    public const string Colt1911WorldModelPath =
        "content/objects3D/asrc_fps/asrc_colt_1911_world.kn5";
    public static readonly string[] Colt1911AnimationPaths =
    [
        "content/objects3D/asrc_fps/asrc_colt_1911_idle.ksanim",
        "content/objects3D/asrc_fps/asrc_colt_1911_fire.ksanim",
        "content/objects3D/asrc_fps/asrc_colt_1911_equip.ksanim",
        "content/objects3D/asrc_fps/asrc_colt_1911_sprint.ksanim",
        "content/objects3D/asrc_fps/asrc_colt_1911_reload.ksanim",
    ];
    public const string Colt1911AttributionPath =
        "content/objects3D/asrc_fps/attribution/colt-1911.txt";
    public const string FragGrenadeWorldModelPath =
        "content/objects3D/asrc_fps/asrc_frag_grenade_world.kn5";
    public const string FragGrenadeViewmodelPath =
        "content/objects3D/asrc_fps/asrc_frag_grenade_viewmodel.kn5";
    public const string FragGrenadeThrowPath =
        "content/objects3D/asrc_fps/asrc_frag_grenade_throw.ksanim";
    public const string FragGrenadeAttributionPath =
        "content/objects3D/asrc_fps/attribution/frag-grenade.txt";
    public const string StickyGrenadeWorldModelPath =
        "content/objects3D/asrc_fps/asrc_sticky_grenade_world.kn5";
    public const string StickyGrenadeViewmodelPath =
        "content/objects3D/asrc_fps/asrc_sticky_grenade_viewmodel.kn5";
    public const string StickyGrenadeThrowPath =
        "content/objects3D/asrc_fps/asrc_sticky_grenade_throw.ksanim";
    public const string StickyGrenadeAttributionPath =
        "content/objects3D/asrc_fps/attribution/sticky-grenade.txt";
    public const string HudManifestPath = "apps/lua/asrc_fps_hud/manifest.ini";
    public const string HudScriptPath = "apps/lua/asrc_fps_hud/asrc_fps_hud.lua";
    public const string HudWeaponImagePath = "apps/lua/asrc_fps_hud/asrc_carbine_hud.png";

    public static byte[] GetRifleViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_assault_rifle_viewmodel.kn5");

    public static byte[] GetRifleWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_assault_rifle_world.kn5");

    public static byte[] GetCompactSmgViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_compact_smg_viewmodel.kn5");

    public static byte[] GetCompactSmgWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_compact_smg_world.kn5");

    public static IReadOnlyList<(string Path, byte[] Data)> GetCompactSmgAnimations() =>
        CompactSmgAnimationPaths.Select(path => (path, ReadEmbeddedKsanim(
            $"AssettoServer.RaceControl.Core.Assets.Fps.{Path.GetFileName(path)}"))).ToArray();

    public static byte[] GetCompactSmgAttribution() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_compact_smg_attribution.txt");

    public static byte[] GetDesertEagleViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_desert_eagle_viewmodel.kn5");

    public static byte[] GetDesertEagleWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_desert_eagle_world.kn5");

    public static IReadOnlyList<(string Path, byte[] Data)> GetDesertEagleAnimations() =>
        DesertEagleAnimationPaths.Select(path => (path, ReadEmbeddedKsanim(
            $"AssettoServer.RaceControl.Core.Assets.Fps.{Path.GetFileName(path)}"))).ToArray();

    public static byte[] GetDesertEagleAttribution() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_desert_eagle_attribution.txt");

    public static byte[] GetColt1911Viewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_colt_1911_viewmodel.kn5");

    public static byte[] GetColt1911WorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_colt_1911_world.kn5");

    public static IReadOnlyList<(string Path, byte[] Data)> GetColt1911Animations() =>
        Colt1911AnimationPaths.Select(path => (path, ReadEmbeddedKsanim(
            $"AssettoServer.RaceControl.Core.Assets.Fps.{Path.GetFileName(path)}"))).ToArray();

    public static byte[] GetColt1911Attribution() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_colt_1911_attribution.txt");

    public static byte[] GetFragGrenadeViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_frag_grenade_viewmodel.kn5");

    public static byte[] GetFragGrenadeWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_frag_grenade_world.kn5");

    public static byte[] GetFragGrenadeThrow() => ReadEmbeddedKsanim(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_frag_grenade_throw.ksanim");

    public static byte[] GetFragGrenadeAttribution() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_frag_grenade_attribution.txt");

    public static byte[] GetStickyGrenadeViewmodel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_sticky_grenade_viewmodel.kn5");

    public static byte[] GetStickyGrenadeWorldModel() => ReadEmbeddedKn5(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_sticky_grenade_world.kn5");

    public static byte[] GetStickyGrenadeThrow() => ReadEmbeddedKsanim(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_sticky_grenade_throw.ksanim");

    public static byte[] GetStickyGrenadeAttribution() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_sticky_grenade_attribution.txt");

    public static byte[] GetRifleDiffuse() => ReadEmbeddedPng(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_rifle_diffuse.png");

    public static byte[] GetOperatorSkin() => ReadEmbeddedPng(
        "AssettoServer.RaceControl.Core.Assets.Fps.asrc_operator_skin.png");

    public static byte[] GetHudManifest() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.Hud.manifest.ini");

    public static byte[] GetHudScript() => ReadEmbeddedText(
        "AssettoServer.RaceControl.Core.Assets.Fps.Hud.asrc_fps_hud.lua");

    public static byte[] GetHudWeaponImage() => ReadEmbeddedPng(
        "AssettoServer.RaceControl.Core.Assets.Fps.Hud.asrc_carbine_hud.png");

    public static IReadOnlyList<(string Path, byte[] Data)> GetAudioAssets()
    {
        var assets = new List<(string Path, byte[] Data)>();
        var assembly = typeof(FpsClientPackAssets).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(AudioResourcePrefix,
                         StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            string fileName = resourceName[AudioResourcePrefix.Length..];
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded FPS audio asset was not found: {resourceName}");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            assets.Add(($"{AudioAssetDirectory}{fileName}", output.ToArray()));
        }

        ValidateAudioAssetSet(assets);
        return assets;
    }

    public static IReadOnlyList<(string Path, byte[] Data)> GetModernAssets()
    {
        var assets = new List<(string Path, byte[] Data)>();
        var assembly = typeof(FpsClientPackAssets).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ModernResourcePrefix,
                         StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            string fileName = resourceName[ModernResourcePrefix.Length..];
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded Modern FPS client asset was not found: {resourceName}");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            byte[] data = output.ToArray();
            ValidateModernAsset(fileName, data);
            assets.Add(($"{ModernAssetDirectory}{fileName}", data));
        }

        if (assets.Count < 30)
            throw new InvalidDataException("Embedded Modern FPS client asset set is incomplete");
        ValidateModernAssetSet(assets);
        return assets;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] ReadEmbeddedKn5(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        if (data.Length < 1024 || !data.AsSpan(0, 6).SequenceEqual("sc6969"u8))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid KN5: {resourceName}");
        return data;
    }

    private static byte[] ReadEmbeddedPng(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        ReadOnlySpan<byte> pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (data.Length < 1024 || !data.AsSpan(0, pngMagic.Length).SequenceEqual(pngMagic))
            throw new InvalidDataException($"Embedded FPS client asset is not a valid PNG: {resourceName}");
        return data;
    }

    private static byte[] ReadEmbeddedKsanim(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client animation was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        if (data.Length < 32 || BitConverter.ToUInt32(data, 0) != 2)
            throw new InvalidDataException(
                $"Embedded FPS client animation is not a valid KSANIM: {resourceName}");
        return data;
    }

    private static byte[] ReadEmbeddedText(string resourceName)
    {
        using Stream stream = typeof(FpsClientPackAssets).Assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException(
                                  $"Embedded FPS client asset was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] data = output.ToArray();
        if (data.Length < 32 || data.Any(value => value == 0))
            throw new InvalidDataException($"Embedded FPS client text asset is invalid: {resourceName}");
        return data;
    }

    private static void ValidateModernAsset(string fileName, byte[] data)
    {
        if (data.Length < 32)
            throw new InvalidDataException($"Embedded Modern FPS asset is too small: {fileName}");
        bool valid;
        if (fileName.EndsWith(".kn5", StringComparison.OrdinalIgnoreCase))
            valid = data.AsSpan(0, 6).SequenceEqual("sc6969"u8);
        else if (fileName.EndsWith(".ksanim", StringComparison.OrdinalIgnoreCase))
            valid = BitConverter.ToUInt32(data, 0) == 2;
        else if (fileName.Equals("asrc-modern-assets.json", StringComparison.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(data);
            valid = document.RootElement.GetProperty("schemaVersion").GetInt32() == 1
                    && document.RootElement.GetProperty("theme").GetString() == "Modern"
                    && document.RootElement.GetProperty("validation").GetProperty("status")
                        .GetString() == "passed";
        }
        else valid = false;
        if (!valid)
            throw new InvalidDataException($"Embedded Modern FPS asset is invalid: {fileName}");
    }

    private static void ValidateModernAssetSet(
        IReadOnlyList<(string Path, byte[] Data)> assets)
    {
        var byName = assets.ToDictionary(asset => Path.GetFileName(asset.Path),
            asset => asset.Data, StringComparer.Ordinal);
        if (!byName.TryGetValue("asrc-modern-assets.json", out byte[]? manifestData))
            throw new InvalidDataException("Embedded Modern FPS manifest is missing");
        using var document = System.Text.Json.JsonDocument.Parse(manifestData);
        System.Text.Json.JsonElement root = document.RootElement;
        if (!root.GetProperty("redistributionRightsConfirmedByUser").GetBoolean()
            || root.GetProperty("sources").GetProperty("m4a1Used").GetBoolean())
            throw new InvalidDataException("Embedded Modern FPS provenance is invalid");
        System.Text.Json.JsonElement files = root.GetProperty("files");
        if (files.GetRawText().Length == 0 || files.EnumerateObject().Count() != byName.Count - 1)
            throw new InvalidDataException("Embedded Modern FPS manifest file count is invalid");
        foreach (System.Text.Json.JsonProperty file in files.EnumerateObject())
        {
            if (!byName.TryGetValue(file.Name, out byte[]? data)
                || !Sha256(data).Equals(file.Value.GetString(), StringComparison.Ordinal))
                throw new InvalidDataException($"Embedded Modern FPS hash mismatch: {file.Name}");
        }
        if (root.GetProperty("operator").GetProperty("triangles").GetInt32() > 40_000
            || root.GetProperty("operator").GetProperty("materials").GetInt32() > 4
            || root.GetProperty("viewmodel").GetProperty("triangles").GetInt32() > 30_000
            || root.GetProperty("viewmodel").GetProperty("materials").GetInt32() > 3
            || root.GetProperty("pickup").GetProperty("triangles").GetInt32() > 6_000
            || root.GetProperty("pickup").GetProperty("materials").GetInt32() != 1
            || root.GetProperty("validation").GetProperty("viewmodelSkinnedMeshes").GetInt32() < 2
            || root.GetProperty("validation").GetProperty("viewmodelWeaponSkinnedMeshes").GetInt32() != 1
            || root.GetProperty("validation").GetProperty("pickupRigidMeshes").GetInt32() != 1
            || !root.GetProperty("validation").GetProperty("stancePosesValidated").GetBoolean()
            || !root.GetProperty("validation").GetProperty("deathCollapseValidated").GetBoolean()
            || !root.GetProperty("validation").GetProperty("uniqueNodeNames").GetBoolean())
            throw new InvalidDataException("Embedded Modern FPS model integrity is invalid");
    }

    private static void ValidateAudioAssetSet(
        IReadOnlyList<(string Path, byte[] Data)> assets)
    {
        var waves = assets.Where(asset => asset.Path.EndsWith(".wav",
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (waves.Length != 54 || assets.Count != 56)
            throw new InvalidDataException(
                $"Embedded FPS audio set must contain 54 WAVs, a manifest and a notice; found {assets.Count} assets");

        byte[] manifestData = assets.Single(asset => asset.Path == AudioManifestPath).Data;
        byte[] noticeData = assets.Single(asset => asset.Path == AudioNoticePath).Data;
        if (noticeData.Length < 200 || noticeData.Any(value => value == 0))
            throw new InvalidDataException("Embedded FPS audio notice is invalid");

        using var document = System.Text.Json.JsonDocument.Parse(manifestData);
        System.Text.Json.JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || root.GetProperty("catalogVersion").GetInt32() != 1
            || !root.GetProperty("generator").GetProperty(
                "commercialLicenseConfirmedByUser").GetBoolean())
            throw new InvalidDataException("Embedded FPS audio manifest header is invalid");
        var clips = root.GetProperty("clips").EnumerateArray().ToArray();
        if (clips.Length != 54)
            throw new InvalidDataException("Embedded FPS audio manifest must contain 54 clips");
        var clipsByPath = clips.ToDictionary(clip => clip.GetProperty("path").GetString()
                                                    ?? string.Empty,
            StringComparer.Ordinal);
        foreach ((string path, byte[] data) in waves)
        {
            if (!clipsByPath.TryGetValue(path, out System.Text.Json.JsonElement clip))
                throw new InvalidDataException($"Embedded FPS WAV is missing from its manifest: {path}");
            bool recorded = clip.TryGetProperty("source", out var source);
            if (recorded)
            {
                if (source.GetProperty("license").GetString() != "CC0-1.0"
                    || string.IsNullOrWhiteSpace(source.GetProperty("author").GetString())
                    || !Uri.TryCreate(source.GetProperty("url").GetString(), UriKind.Absolute, out _)
                    || !Uri.TryCreate(source.GetProperty("downloadUrl").GetString(), UriKind.Absolute, out _)
                    || source.GetProperty("sha256").GetString()?.Length != 64
                    || string.IsNullOrWhiteSpace(source.GetProperty("processing").GetString())
                    || !DateTimeOffset.TryParse(clip.GetProperty("importedAtUtc").GetString(), out _))
                    throw new InvalidDataException($"Embedded FPS recording provenance is incomplete: {path}");
            }
            else if (string.IsNullOrWhiteSpace(clip.GetProperty("prompt").GetString())
                || clip.GetProperty("model").GetString()
                != root.GetProperty("generator").GetProperty("model").GetString()
                || !DateTimeOffset.TryParse(clip.GetProperty("generatedAtUtc").GetString(),
                    out _))
                throw new InvalidDataException(
                    $"Embedded FPS WAV provenance is incomplete: {path}");
            double targetPeakDb = recorded && clip.GetProperty("category").GetString() == "locomotion"
                ? -6 : -1;
            FpsWaveMetadata metadata = ValidateWave(path, data, targetPeakDb);
            if (clip.GetProperty("sha256").GetString() != Sha256(data)
                || clip.GetProperty("sampleRate").GetInt32() != metadata.SampleRate
                || clip.GetProperty("channels").GetInt32() != metadata.Channels
                || clip.GetProperty("codec").GetString() != "pcm_s16le"
                || Math.Abs(clip.GetProperty("peakDb").GetDouble() - metadata.PeakDb) > 0.05
                || Math.Abs(clip.GetProperty("durationSeconds").GetDouble()
                            - metadata.DurationSeconds) > 0.002)
                throw new InvalidDataException($"Embedded FPS WAV metadata does not match: {path}");
        }
    }

    private static FpsWaveMetadata ValidateWave(string path, byte[] data, double targetPeakDb)
    {
        if (data.Length < 44 || !data.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                             || !data.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException($"Embedded FPS audio is not a RIFF/WAVE file: {path}");
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.ASCII);
        reader.BaseStream.Position = 12;
        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        int dataBytes = 0;
        int dataOffset = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int length = reader.ReadInt32();
            long next = reader.BaseStream.Position + length + (length & 1);
            if (length < 0 || next > reader.BaseStream.Length)
                throw new InvalidDataException($"Embedded FPS WAV has an invalid chunk: {path}");
            if (chunk == "fmt " && length >= 16)
            {
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.BaseStream.Position += 6;
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunk == "data")
            {
                dataBytes = length;
                dataOffset = checked((int)reader.BaseStream.Position);
            }
            reader.BaseStream.Position = next;
        }
        if (audioFormat != 1 || channels != 1 || sampleRate != 44_100
            || bitsPerSample != 16 || dataBytes <= 0)
            throw new InvalidDataException(
                $"Embedded FPS WAV must be mono 44.1 kHz 16-bit PCM: {path}");
        int peak = 0;
        for (int offset = dataOffset; offset + 1 < dataOffset + dataBytes; offset += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(data, offset)));
        double peakDb = peak > 0 ? 20 * Math.Log10(peak / 32768d) : double.NegativeInfinity;
        if (peakDb < targetPeakDb - 0.1 || peakDb > targetPeakDb + 0.2)
            throw new InvalidDataException(
                $"Embedded FPS WAV peak must be normalized to {targetPeakDb} dBFS: {path} ({peakDb:F2} dBFS)");
        return new FpsWaveMetadata(sampleRate, channels,
            (double)dataBytes / (sampleRate * channels * (bitsPerSample / 8)), peakDb);
    }

    private readonly record struct FpsWaveMetadata(int SampleRate, int Channels,
        double DurationSeconds, double PeakDb);
}
