using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AssettoServer.RaceControl.Core.Content;

public static class InstallationLocator
{
    [SupportedOSPlatform("windows")]
    public static string? FindAssettoCorsaRoot()
    {
        var candidates = new List<string>
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa",
            @"C:\Program Files\Steam\steamapps\common\assettocorsa",
        };

        AddSteamCandidate(candidates, Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"));
        AddSteamCandidate(candidates, Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam"));

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(IsAssettoCorsaRoot);
    }

    public static bool IsAssettoCorsaRoot(string? path) => !string.IsNullOrWhiteSpace(path)
        && Directory.Exists(Path.Combine(path, "content", "cars"))
        && Directory.Exists(Path.Combine(path, "content", "tracks"));

    [SupportedOSPlatform("windows")]
    private static void AddSteamCandidate(List<string> candidates, RegistryKey? key)
    {
        using (key)
        {
            var root = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(Path.Combine(root, "steamapps", "common", "assettocorsa"));
            }
        }
    }
}
