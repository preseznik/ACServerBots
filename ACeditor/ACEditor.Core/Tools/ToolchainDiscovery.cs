using Microsoft.Win32;

namespace ACEditor.Core.Tools;

public sealed class ToolchainPaths
{
    public string? AssettoCorsaRoot { get; set; }
    public string? Dirt2Root { get; set; }
    public string? EgoPssgEditorRoot { get; set; }
    public string? BlenderExecutable { get; set; }
    public string? TexconvExecutable { get; set; }
    public string? KsEditorExecutable { get; set; }
}

public sealed class ToolchainDiscovery
{
    public ToolchainPaths Discover()
    {
        ToolchainPaths overrides;
        try { overrides = new ToolchainSettingsStore().LoadApplicationSettings().Toolchain; }
        catch (InvalidDataException) { overrides = new ToolchainPaths(); }
        string steamRoot = ReadRegistryString(Registry.CurrentUser,
            @"Software\Valve\Steam", "SteamPath")?.Replace('/', '\\') ??
            @"C:\Program Files (x86)\Steam";
        var paths = new ToolchainPaths
        {
            AssettoCorsaRoot = FirstDirectory(
                overrides.AssettoCorsaRoot,
                Environment.GetEnvironmentVariable("ASSETTO_CORSA_ROOT"),
                Path.Combine(steamRoot, "steamapps", "common", "assettocorsa")),
            Dirt2Root = FirstDirectory(
                overrides.Dirt2Root,
                Environment.GetEnvironmentVariable("DIRT2_ROOT"),
                Path.Combine(steamRoot, "steamapps", "common", "DiRT 2")),
            EgoPssgEditorRoot = FirstDirectory(
                overrides.EgoPssgEditorRoot,
                Environment.GetEnvironmentVariable("EGO_PSSG_EDITOR_ROOT"),
                @"F:\Tools 3rd Party\Ego PSSG Editor"),
            BlenderExecutable = FirstFile(
                overrides.BlenderExecutable,
                Environment.GetEnvironmentVariable("BLENDER_EXE"),
                @"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
                @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
                @"C:\Program Files\Blender Foundation\Blender 4.4\blender.exe"),
            TexconvExecutable = FirstFile(overrides.TexconvExecutable,
                Environment.GetEnvironmentVariable("TEXCONV_EXE"))
        };
        paths.KsEditorExecutable = FirstFile(overrides.KsEditorExecutable, paths.AssettoCorsaRoot is null ? null :
            Path.Combine(paths.AssettoCorsaRoot, "sdk", "editor", "ksEditor.exe"));
        return paths;
    }

    private static string? ReadRegistryString(RegistryKey root, string path, string name)
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    private static string? FirstDirectory(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate));
    private static string? FirstFile(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
}
