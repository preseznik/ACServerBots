using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace AssettoServer.Release;

// Release builds read the same AC-root pack distributed to players. Development
// builds retain embedded resources for the existing asset tests and tools.
public static class FpsAssetResources
{
    private const string CorePrefix = "AssettoServer.RaceControl.Core.Assets.Fps.";
    private const string ServerPrefix = "AssettoServer.Server.Fps.Assets.";
    private const string ModernPrefix = "AssettoServer.Server.Fps.ModernAssets.";

    public static string? FindRoot(string? baseDirectory = null)
    {
        string root = baseDirectory ?? AppContext.BaseDirectory;
        string? configured = AppContext.GetData("ASRC.FpsAssetRoot") as string;
        string[] candidates = configured is null
            ? new[] { Path.Combine(root, "Data", "Packs", "Fps"), Path.Combine(root, "Packs", "Fps"),
                Path.Combine(root, "..", "..", "Packs", "Fps") }
            : new[] { configured };
        return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "asrc-fps-client.json")));
    }

    public static void ValidateRoot(string root)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "asrc-fps-client.json")));
        if (manifest.RootElement.GetProperty("clientPackVersion").GetInt32() != ReleaseIdentity.FpsPackVersion)
            throw new InvalidDataException($"Install FPS client pack {ReleaseIdentity.FpsPackVersion} for this release.");
    }

    public static bool IsAvailable(Assembly assembly) => FindRoot() is not null
        || assembly.GetManifestResourceNames().Any(name => name.EndsWith(
            ".asrc_assault_rifle_viewmodel.kn5", StringComparison.Ordinal));

    public static Stream? Open(Assembly assembly, string name)
    {
        var embedded = assembly.GetManifestResourceStream(name);
        if (embedded is not null) return embedded;
        string root = FindRoot() ?? throw new FileNotFoundException(
            "FPS assets are not installed. Open Local Installations and install the FPS client pack.");
        ValidateRoot(root);
        return File.OpenRead(Path.Combine(root, RelativePath(name)));
    }

    public static string[] Names(Assembly assembly)
    {
        string[] embedded = assembly.GetManifestResourceNames();
        if (embedded.Any(name => name.StartsWith(CorePrefix, StringComparison.Ordinal)
                                 || name.StartsWith(ModernPrefix, StringComparison.Ordinal)))
            return embedded;
        string? root = FindRoot();
        if (root is null) return embedded;
        ValidateRoot(root);
        bool core = assembly.GetName().Name == "AssettoServer.RaceControl.Core";
        var names = new List<string>(embedded);
        Add("content/objects3D/asrc_fps/modern", core ? CorePrefix + "Modern." : ModernPrefix);
        if (core) Add("extension/audio/asrc_fps", CorePrefix + "Audio.");
        return names.ToArray();

        void Add(string relative, string prefix)
        {
            string directory = Path.Combine(root, relative);
            if (Directory.Exists(directory))
                names.AddRange(Directory.EnumerateFiles(directory).Select(path => prefix + Path.GetFileName(path)));
        }
    }

    private static string RelativePath(string name)
    {
        string suffix;
        if (name.StartsWith(ModernPrefix, StringComparison.Ordinal))
            return "content/objects3D/asrc_fps/modern/" + name[ModernPrefix.Length..];
        if (name.StartsWith(CorePrefix, StringComparison.Ordinal)) suffix = name[CorePrefix.Length..];
        else if (name.StartsWith(ServerPrefix, StringComparison.Ordinal)) suffix = name[ServerPrefix.Length..];
        else throw new InvalidDataException($"Unknown FPS resource: {name}");
        if (suffix.Contains('/') || suffix.Contains('\\') || suffix.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid FPS resource path.");
        if (suffix.StartsWith("Modern.", StringComparison.Ordinal))
            return "content/objects3D/asrc_fps/modern/" + suffix[7..];
        if (suffix.StartsWith("Audio.", StringComparison.Ordinal))
            return "extension/audio/asrc_fps/" + suffix[6..];
        if (suffix.StartsWith("Hud.", StringComparison.Ordinal))
            return "apps/lua/asrc_fps_hud/" + suffix[4..];
        if (suffix == "asrc_carbine_hud.png") return "apps/lua/asrc_fps_hud/" + suffix;
        string? attribution = suffix switch
        {
            "asrc_compact_smg_attribution.txt" => "compact-smg",
            "asrc_desert_eagle_attribution.txt" => "desert-eagle",
            "asrc_colt_1911_attribution.txt" => "colt-1911",
            "asrc_frag_grenade_attribution.txt" => "frag-grenade",
            "asrc_sticky_grenade_attribution.txt" => "sticky-grenade",
            _ => null
        };
        return attribution is null ? "content/objects3D/asrc_fps/" + suffix
            : "content/objects3D/asrc_fps/attribution/" + attribution + ".txt";
    }
}
