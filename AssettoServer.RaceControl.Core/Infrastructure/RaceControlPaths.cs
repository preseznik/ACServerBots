namespace AssettoServer.RaceControl.Core.Infrastructure;

public sealed class RaceControlPaths
{
    public RaceControlPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AssettoServer Race Control");
    }

    public string DataRoot { get; }
    public string PresetsDirectory => Path.Combine(DataRoot, "Presets");
    public string InstancesDirectory => Path.Combine(DataRoot, "Instances");
    public string CacheDirectory => Path.Combine(DataRoot, "Cache");
    public string LogsDirectory => Path.Combine(DataRoot, "Logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(InstancesDirectory);
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
