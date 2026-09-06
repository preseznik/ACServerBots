namespace AssettoServer.RaceControl.Core.Infrastructure;

public sealed class RaceControlPaths
{
    public RaceControlPaths(string? dataRoot = null, string? packageRoot = null)
    {
        PackageRoot = Path.GetFullPath(packageRoot ?? AppContext.BaseDirectory);
        IsPortable = File.Exists(Path.Combine(PackageRoot, "portable.json"));
        DataRoot = Path.GetFullPath(dataRoot ?? (IsPortable ? Path.Combine(PackageRoot, "Data") : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AssettoServer Race Control")));
        if (IsPortable) RequireInsidePackage(DataRoot);
    }

    public string PackageRoot { get; }
    public bool IsPortable { get; }
    public string DataRoot { get; }
    public string TempDirectory => Path.Combine(DataRoot, "Temp");
    public string ExportsDirectory => Path.Combine(DataRoot, "Exports");
    public string DownloadsDirectory => Path.Combine(DataRoot, "Downloads");
    public string ServersDirectory => Path.Combine(DataRoot, "Servers");
    public string FpsAssetsDirectory => Path.Combine(DataRoot, "Packs", "Fps");
    public string FpsMapsDirectory => Path.Combine(DataRoot, "Packs", "FpsMaps");

    public string StoreComponentPath(string path) => IsPortable && !string.IsNullOrWhiteSpace(path)
        && IsInsidePackage(path) ? Path.GetRelativePath(PackageRoot, path) : path;

    public string ResolveComponentPath(string path) => string.IsNullOrWhiteSpace(path) ? path
        : Path.IsPathRooted(path) ? path : Path.GetFullPath(path, PackageRoot);

    public bool IsInsidePackage(string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(PackageRoot) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    public void RequireInsidePackage(string path)
    {
        if (!IsPortable) return;
        if (!IsInsidePackage(path))
            throw new IOException("Portable files must stay inside the extracted Race Control folder.");
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        while (directory is not null && IsInsidePackage(directory.FullName))
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Portable storage cannot pass through a directory link.");
            directory = directory.Parent;
        }
    }

    public void ConfigureProcessStorage()
    {
        EnsureCreated();
        if (!IsPortable) return;
        Environment.SetEnvironmentVariable("TEMP", TempDirectory);
        Environment.SetEnvironmentVariable("TMP", TempDirectory);
        Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", DataRoot);
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics", "0");
    }
    public string PresetsDirectory => Path.Combine(DataRoot, "Presets");
    public string GridsDirectory => Path.Combine(DataRoot, "Grids");
    public string FpsArenasDirectory => Path.Combine(DataRoot, "FpsArenas");
    public string FpsClientPacksDirectory => Path.Combine(DataRoot, "FpsClientPacks");
    public string InstancesDirectory => Path.Combine(DataRoot, "Instances");
    public string WorkingInstanceDirectory => Path.Combine(InstancesDirectory, "Current");
    public string HistoryDirectory => Path.Combine(DataRoot, "History");
    public string CacheDirectory => Path.Combine(DataRoot, "Cache");
    public string LogsDirectory => Path.Combine(DataRoot, "Logs");

    public void EnsureCreated()
    {
        foreach (string directory in new[] { DataRoot, TempDirectory, ExportsDirectory,
                     PresetsDirectory, GridsDirectory, FpsArenasDirectory, FpsClientPacksDirectory,
                     InstancesDirectory, WorkingInstanceDirectory, HistoryDirectory, CacheDirectory, LogsDirectory })
            RequireInsidePackage(directory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(GridsDirectory);
        Directory.CreateDirectory(FpsArenasDirectory);
        Directory.CreateDirectory(FpsClientPacksDirectory);
        Directory.CreateDirectory(InstancesDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public string GetInstanceDirectory(string name, Guid id)
    {
        var slug = FileNameSanitizer.Slug(name);
        var shortId = id.ToString("N")[..8];
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(InstancesDirectory, $"{slug}-{shortId}-{timestamp}");
    }
}

public static class FileNameSanitizer
{
    public static string Slug(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Trim().ToLowerInvariant()
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character)
            .ToArray());

        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        result = result.Trim('-', '.');
        if (result.Length > 72)
        {
            result = result[..72].TrimEnd('-', '.');
        }
        return string.IsNullOrEmpty(result) ? "preset" : result;
    }
}
