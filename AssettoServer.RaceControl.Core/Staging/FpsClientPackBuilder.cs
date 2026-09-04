using System.IO.Compression;
using System.Text.Json;

namespace AssettoServer.RaceControl.Core.Staging;

public static class FpsClientPackBuilder
{
    public const int ClientPackVersion = 30;
    public const int BridgeProtocol = 5;
    public const string DefaultFileName = "asrc-fps-compatibility-client-v30.zip";
    public const string MinimumCspVersion = "0.3.0-preview520";

    public static async Task WriteAsync(Stream destination, string carrierCarId,
        CancellationToken cancellationToken = default)
    {
        byte[] rifleViewmodel = FpsClientPackAssets.GetRifleViewmodel();
        byte[] rifleWorldModel = FpsClientPackAssets.GetRifleWorldModel();
        byte[] compactSmgViewmodel = FpsClientPackAssets.GetCompactSmgViewmodel();
        byte[] compactSmgWorldModel = FpsClientPackAssets.GetCompactSmgWorldModel();
        IReadOnlyList<(string Path, byte[] Data)> compactSmgAnimations =
            FpsClientPackAssets.GetCompactSmgAnimations();
        byte[] compactSmgAttribution = FpsClientPackAssets.GetCompactSmgAttribution();
        byte[] desertEagleViewmodel = FpsClientPackAssets.GetDesertEagleViewmodel();
        byte[] desertEagleWorldModel = FpsClientPackAssets.GetDesertEagleWorldModel();
        IReadOnlyList<(string Path, byte[] Data)> desertEagleAnimations =
            FpsClientPackAssets.GetDesertEagleAnimations();
        byte[] desertEagleAttribution = FpsClientPackAssets.GetDesertEagleAttribution();
        byte[] colt1911Viewmodel = FpsClientPackAssets.GetColt1911Viewmodel();
        byte[] colt1911WorldModel = FpsClientPackAssets.GetColt1911WorldModel();
        IReadOnlyList<(string Path, byte[] Data)> colt1911Animations =
            FpsClientPackAssets.GetColt1911Animations();
        byte[] colt1911Attribution = FpsClientPackAssets.GetColt1911Attribution();
        byte[] fragGrenadeViewmodel = FpsClientPackAssets.GetFragGrenadeViewmodel();
        byte[] fragGrenadeWorldModel = FpsClientPackAssets.GetFragGrenadeWorldModel();
        byte[] fragGrenadeThrow = FpsClientPackAssets.GetFragGrenadeThrow();
        byte[] fragGrenadeAttribution = FpsClientPackAssets.GetFragGrenadeAttribution();
        byte[] stickyGrenadeViewmodel = FpsClientPackAssets.GetStickyGrenadeViewmodel();
        byte[] stickyGrenadeWorldModel = FpsClientPackAssets.GetStickyGrenadeWorldModel();
        byte[] stickyGrenadeThrow = FpsClientPackAssets.GetStickyGrenadeThrow();
        byte[] stickyGrenadeAttribution = FpsClientPackAssets.GetStickyGrenadeAttribution();
        byte[] rifleDiffuse = FpsClientPackAssets.GetRifleDiffuse();
        byte[] operatorSkin = FpsClientPackAssets.GetOperatorSkin();
        byte[] hudManifest = FpsClientPackAssets.GetHudManifest();
        byte[] hudScript = FpsClientPackAssets.GetHudScript();
        byte[] hudWeaponImage = FpsClientPackAssets.GetHudWeaponImage();
        byte[] rifleAudio = FpsClientPackAssets.CreateRifleWave();
        byte[] explosionAudio = FpsClientPackAssets.CreateExplosionWave();
        IReadOnlyList<(string Path, byte[] Data)> modernAssets =
            FpsClientPackAssets.GetModernAssets();

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var manifestEntry = archive.CreateEntry("asrc-fps-client.json", CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, new
            {
                protocol = 2,
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
                loadoutItems = new object[]
                {
                    new { id = 2, name = "MP5 SMG", kind = "main", placeholder = false,
                        viewmodelPath = FpsClientPackAssets.CompactSmgViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(compactSmgViewmodel),
                        worldModelPath = FpsClientPackAssets.CompactSmgWorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(compactSmgWorldModel),
                        animations = compactSmgAnimations.Select(asset => new
                        {
                            path = asset.Path,
                            sha256 = FpsClientPackAssets.Sha256(asset.Data),
                        }),
                        attributionPath = FpsClientPackAssets.CompactSmgAttributionPath,
                        attributionSha256 = FpsClientPackAssets.Sha256(compactSmgAttribution) },
                    new { id = 3, name = "Desert Eagle", kind = "secondary", placeholder = false,
                        viewmodelPath = FpsClientPackAssets.DesertEagleViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(desertEagleViewmodel),
                        worldModelPath = FpsClientPackAssets.DesertEagleWorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(desertEagleWorldModel),
                        animations = desertEagleAnimations.Select(asset => new
                        {
                            path = asset.Path,
                            sha256 = FpsClientPackAssets.Sha256(asset.Data),
                        }),
                        attributionPath = FpsClientPackAssets.DesertEagleAttributionPath,
                        attributionSha256 = FpsClientPackAssets.Sha256(desertEagleAttribution) },
                    new { id = 4, name = "Colt 1911", kind = "secondary", placeholder = false,
                        viewmodelPath = FpsClientPackAssets.Colt1911ViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(colt1911Viewmodel),
                        worldModelPath = FpsClientPackAssets.Colt1911WorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(colt1911WorldModel),
                        animations = colt1911Animations.Select(asset => new
                        {
                            path = asset.Path,
                            sha256 = FpsClientPackAssets.Sha256(asset.Data),
                        }),
                        attributionPath = FpsClientPackAssets.Colt1911AttributionPath,
                        attributionSha256 = FpsClientPackAssets.Sha256(colt1911Attribution) },
                    new { id = 16, name = "Frag Grenade", kind = "lethal", placeholder = false,
                        viewmodelPath = FpsClientPackAssets.FragGrenadeViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(fragGrenadeViewmodel),
                        worldModelPath = FpsClientPackAssets.FragGrenadeWorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(fragGrenadeWorldModel),
                        throwAnimationPath = FpsClientPackAssets.FragGrenadeThrowPath,
                        throwAnimationSha256 = FpsClientPackAssets.Sha256(fragGrenadeThrow),
                        attributionPath = FpsClientPackAssets.FragGrenadeAttributionPath,
                        attributionSha256 = FpsClientPackAssets.Sha256(fragGrenadeAttribution) },
                    new { id = 17, name = "Sticky Grenade", kind = "lethal", placeholder = false,
                        viewmodelPath = FpsClientPackAssets.StickyGrenadeViewmodelPath,
                        viewmodelSha256 = FpsClientPackAssets.Sha256(stickyGrenadeViewmodel),
                        worldModelPath = FpsClientPackAssets.StickyGrenadeWorldModelPath,
                        worldModelSha256 = FpsClientPackAssets.Sha256(stickyGrenadeWorldModel),
                        throwAnimationPath = FpsClientPackAssets.StickyGrenadeThrowPath,
                        throwAnimationSha256 = FpsClientPackAssets.Sha256(stickyGrenadeThrow),
                        attributionPath = FpsClientPackAssets.StickyGrenadeAttributionPath,
                        attributionSha256 = FpsClientPackAssets.Sha256(stickyGrenadeAttribution) },
                },
                operatorSkinPath = FpsClientPackAssets.OperatorSkinPath,
                operatorSkinSha256 = FpsClientPackAssets.Sha256(operatorSkin),
                hud = new
                {
                    app = "ASRC FPS HUD",
                    bridge = "asrc.fps.hud.v5",
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
                models, the CC BY MP5 SMG, Desert Eagle and Colt 1911 with reused
                skinned carbine arms, weapon-specific hand poses and magazine motion,
                real M67 and Semtex-style grenade models, first-person throw animations and
                operator UV skin under
                content/objects3D/asrc_fps, plus rifle sound
                under extension/audio/asrc_fps. It also installs the presentation-only ASRC FPS
                HUD under apps/lua/asrc_fps_hud. Client pack v30 also contains the animated Modern
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
        await WriteEntryAsync(archive, FpsClientPackAssets.CompactSmgViewmodelPath,
            compactSmgViewmodel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.CompactSmgWorldModelPath,
            compactSmgWorldModel, cancellationToken);
        foreach ((string path, byte[] data) in compactSmgAnimations)
            await WriteEntryAsync(archive, path, data, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.CompactSmgAttributionPath,
            compactSmgAttribution, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.DesertEagleViewmodelPath,
            desertEagleViewmodel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.DesertEagleWorldModelPath,
            desertEagleWorldModel, cancellationToken);
        foreach ((string path, byte[] data) in desertEagleAnimations)
            await WriteEntryAsync(archive, path, data, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.DesertEagleAttributionPath,
            desertEagleAttribution, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.Colt1911ViewmodelPath,
            colt1911Viewmodel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.Colt1911WorldModelPath,
            colt1911WorldModel, cancellationToken);
        foreach ((string path, byte[] data) in colt1911Animations)
            await WriteEntryAsync(archive, path, data, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.Colt1911AttributionPath,
            colt1911Attribution, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.FragGrenadeViewmodelPath,
            fragGrenadeViewmodel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.FragGrenadeWorldModelPath,
            fragGrenadeWorldModel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.FragGrenadeThrowPath,
            fragGrenadeThrow, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.FragGrenadeAttributionPath,
            fragGrenadeAttribution, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.StickyGrenadeViewmodelPath,
            stickyGrenadeViewmodel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.StickyGrenadeWorldModelPath,
            stickyGrenadeWorldModel, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.StickyGrenadeThrowPath,
            stickyGrenadeThrow, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.StickyGrenadeAttributionPath,
            stickyGrenadeAttribution, cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudManifestPath, hudManifest,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudScriptPath, hudScript,
            cancellationToken);
        await WriteEntryAsync(archive, FpsClientPackAssets.HudWeaponImagePath, hudWeaponImage,
            cancellationToken);
        await WriteEntryAsync(archive, "extension/audio/asrc_fps/rifle.wav", rifleAudio,
            cancellationToken);
        await WriteEntryAsync(archive, "extension/audio/asrc_fps/explosion.wav", explosionAudio,
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
