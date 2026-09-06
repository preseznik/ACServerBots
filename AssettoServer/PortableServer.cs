using System;
using System.IO;
using System.Linq;

namespace AssettoServer;

internal static class PortableServer
{
    public static string? Root { get; private set; }

    public static void Initialize()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "portable.json")))
            {
                Root = directory.FullName;
                break;
            }
            directory = directory.Parent;
        }
        if (Root is null) return;
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        string temporary = Path.Combine(Root, "Data", "Temp");
        CheckWritePath(temporary);
        foreach (string relative in new[] { "logs", "crash", "data-protection-keys", "cfg", "presets" })
            CheckWritePath(Path.Combine(AppContext.BaseDirectory, relative));
        Directory.CreateDirectory(temporary);
        Environment.SetEnvironmentVariable("TEMP", temporary);
        Environment.SetEnvironmentVariable("TMP", temporary);
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics", "0");
    }

    public static void CheckWritePath(string? path)
    {
        if (Root is null || string.IsNullOrWhiteSpace(path)) return;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("Portable server outputs must stay inside the extracted package.");
        var directory = new DirectoryInfo(full);
        while (directory is not null && directory.FullName.Length > Root.Length)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Portable outputs cannot pass through a directory link.");
            directory = directory.Parent;
        }
    }
}
