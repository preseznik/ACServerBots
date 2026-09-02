using System.IO.Compression;
using System.Text.Json;

namespace AssettoServer.RaceControl.Core.Staging;

public static class FpsClientPackBuilder
{
    public const int ClientPackVersion = 17;
    public const int BridgeProtocol = 4;
    public const string DefaultFileName = "asrc-fps-compatibility-client-v17.zip";
    public const string MinimumCspVersion = "0.3.0-preview520";

    public static async Task WriteAsync(Stream destination, string carrierCarId,
        CancellationToken cancellationToken = default)
    {
        byte[] rifleViewmodel = FpsClientPackAssets.GetRifleViewmodel();
        byte[] rifleWorldModel = FpsClientPackAssets.GetRifleWorldModel();
        byte[] rifleDiffuse = FpsClientPackAssets.GetRifleDiffuse();
        byte[] operatorSkin = FpsClientPackAssets.GetOperatorSkin();
        byte[] hudManifest = FpsClientPackAssets.GetHudManifest();
        byte[] hudScript = FpsClientPackAssets.GetHudScript();
        byte[] hudWeaponImage = FpsClientPackAssets.GetHudWeaponImage();
        byte[] rifleAudio = FpsClientPackAssets.CreateRifleWave();
        IReadOnlyList<(string Path, byte[] Data)> modernAssets =
            FpsClientPackAssets.GetModernAssets();

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var manifestEntry = archive.CreateEntry("asrc-fps-client.json", CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, new
            {
                protocol = 1,
                clientPackVersion = ClientPackVersion,
                compatibilityGate = true,
                minimumCspVersion = MinimumCspVersion,
                carrierCar = carrierCarId,
                nativeHooks = false,
                visualThemes = new
                {
                    defaultTheme = "Blocks",
                    available = new[] { "Blocks", "Modern" },
                    modernAssetDirectory = FpsClientPackAssets.ModernAssetDirectory,
                    modernAssets = modernAssets.Select(asset => new
                    {
                        path = asset.Path,
                        sha256 = FpsClientPackAssets.Sha256(asset.Data),
                    }),
                },
                weapon = new
                {
                    id = "asrc_assault_rifle_v1",
                    ammunition = "40-round magazine with four reserves",
                    fireIntervalSeconds = 0.12,
                    damage = 34,
                    rangeMetres = 120,
                    packagedViewmodel = true,
                    viewmodelPath = FpsClientPackAssets.RifleViewmodelPath,
                    viewmodelSha256 = FpsClientPackAssets.Sha256(rifleViewmodel),
                    worldModelPath = FpsClientPackAssets.RifleWorldModelPath,
                    worldModelSha256 = FpsClientPackAssets.Sha256(rifleWorldModel),
                    diffusePath = FpsClientPackAssets.RifleDiffusePath,
                    diffuseSha256 = FpsClientPackAssets.Sha256(rifleDiffuse),
                },
                operatorSkinPath = FpsClientPackAssets.OperatorSkinPath,
                operatorSkinSha256 = FpsClientPackAssets.Sha256(operatorSkin),
                hud = new
                {
                    app = "ASRC FPS HUD",
                    bridge = "asrc.fps.hud.v4",
                    bridgeProtocol = BridgeProtocol,
                    manifestPath = FpsClientPackAssets.HudManifestPath,
                    manifestSha256 = FpsClientPackAssets.Sha256(hudManifest),
                    scriptPath = FpsClientPackAssets.HudScriptPath,
                    scriptSha256 = FpsClientPackAssets.Sha256(hudScript),
                    weaponImagePath = FpsClientPackAssets.HudWeaponImagePath,
                    weaponImageSha256 = FpsClientPackAssets.Sha256(hudWeaponImage),
                    onlineFallback = true,
                },
            }, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        }

        var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(readmeEntry.Open()))
        {
            await writer.WriteAsync("""
                AssettoServer Race Control FPS compatibility gate

                Requirements:
                - Assetto Corsa with CSP 0.3.0-preview520 or newer compatible preview.
                - The carrier car named in asrc-fps-client.json must be installed.
                - Join through Content Manager Online > LAN.

                The server delivers the CSP online script automatically. Extract this ZIP into
                the Assetto Corsa installation root. It installs the project-owned assault-rifle
                models and operator UV skin under content/objects3D/asrc_fps, plus rifle sound
                under extension/audio/asrc_fps. It also installs the presentation-only ASRC FPS
                HUD under apps/lua/asrc_fps_hud. Client pack v17 also contains the animated Modern
                operator and carbine theme under content/objects3D/asrc_fps/modern. Existing files
                are not replaced outside those project-owned folders. Blocks remains the default;
                the server chooses one theme for the next staged match.

                The HUD app is background-loaded and takes over the exclusive HUD layer only
                while its versioned local bridge is receiving a live FPS session heartbeat.
                The server-delivered online script remains authoritative and restores its full
                fallback HUD within 0.5 seconds if the app is missing, disabled or incompatible.
                Normal AC UI and other apps remain unchanged outside active FPS gameplay.
                FPS mode requests a 0.03 m camera near clip at runtime. If a CSP build or global
                graphics override prevents that request, the client log reports the observed
                near-clip value and method so wall clipping is diagnosable.
                No acs.exe modification or native hook is installed.
                """.AsMemory(), cancellationToken);
        }

        await WriteEntryAsync(archive, FpsClientPackAssets.RifleViewmodelPath, rifleViewmodel,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.RifleWorldModelPath, rifleWorldModel,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.RifleDiffusePath, rifleDiffuse,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.OperatorSkinPath, operatorSkin,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudManifestPath, hudManifest,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudScriptPath, hudScript,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudWeaponImagePath, hudWeaponImage,
            cancellationToken);
        await WriteEntryAsync(archive, "extension/audio/asrc_fps/rifle.wav", rifleAudio,
            cancellationToken);
        foreach ((string path, byte[] data) in modernAssets)
            await WriteEntryAsync(archive, path, data, cancellationToken);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] data,
        CancellationToken cancellationToken)
    {
        ValidateProjectOwnedPath(path);
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await stream.WriteAsync(data, cancellationToken);
    }

    internal static void ValidateProjectOwnedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        bool projectOwned = normalized.StartsWith("content/objects3D/asrc_fps/",
                                StringComparison.Ordinal)
                            || normalized.StartsWith("extension/audio/asrc_fps/",
                                StringComparison.Ordinal)
                            || normalized.StartsWith("apps/lua/asrc_fps_hud/",
                                StringComparison.Ordinal);
        if (!projectOwned || normalized.Contains("../", StringComparison.Ordinal)
                          || normalized.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidDataException($"FPS client-pack path is outside project ownership: {path}");
    }
}
