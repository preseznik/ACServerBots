using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed record LegacyInstance(string Path, string Name, DateTimeOffset? CreatedAt, long SizeBytes)
{
    public string DisplayName => $"{Name}  •  {LegacyInstanceCatalog.FormatSize(SizeBytes)}"
                                 + (CreatedAt is { } created ? $"  •  {created.LocalDateTime:g}" : string.Empty);
}

/// <summary>
/// Finds the timestamped instance folders older builds created under Instances. Newer builds reuse
/// Instances\Current and keep compact copies in History, so these folders are only taking up space.
/// Current, History and anything that doesn't look like an old instance are never listed.
/// </summary>
public sealed partial class LegacyInstanceCatalog(RaceControlPaths paths)
{
    // Older builds named folders <preset slug>-<8 hex id>-<yyyyMMdd-HHmmss> (RaceControlPaths.GetInstanceDirectory).
    [GeneratedRegex(@"-[0-9a-f]{8}-(\d{8}-\d{6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyName();

    public IReadOnlyList<LegacyInstance> List()
    {
        if (!Directory.Exists(paths.InstancesDirectory))
            return [];
        var instances = new List<LegacyInstance>();
        foreach (var directory in new DirectoryInfo(paths.InstancesDirectory).EnumerateDirectories())
        {
            if (!IsLegacyInstance(directory))
                continue;
            instances.Add(new LegacyInstance(directory.FullName, directory.Name, ReadCreatedAt(directory),
                MeasureSize(directory)));
        }
        return instances.OrderBy(instance => instance.CreatedAt ?? DateTimeOffset.MaxValue).ToArray();
    }

    /// <summary>
    /// Guards removal: only a direct, non-linked child of Instances that is a legacy instance, never Current.
    /// </summary>
    public bool CanRemove(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            return directory.Exists
                   && directory.Parent is { } parent
                   && string.Equals(Path.TrimEndingDirectorySeparator(parent.FullName),
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.InstancesDirectory)),
                       StringComparison.OrdinalIgnoreCase)
                   && IsLegacyInstance(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.CurrentCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", CultureInfo.CurrentCulture) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", CultureInfo.CurrentCulture) + " KB",
        _ => bytes.ToString(CultureInfo.CurrentCulture) + " bytes",
    };

    private bool IsLegacyInstance(DirectoryInfo directory)
    {
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || string.Equals(Path.TrimEndingDirectorySeparator(directory.FullName),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.WorkingInstanceDirectory)),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return LegacyName().IsMatch(directory.Name)
               || File.Exists(Path.Combine(directory.FullName, "race-control-instance.json"));
    }

    private static DateTimeOffset? ReadCreatedAt(DirectoryInfo directory)
    {
        try
        {
            string manifest = Path.Combine(directory.FullName, "race-control-instance.json");
            if (File.Exists(manifest))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                if (document.RootElement.TryGetProperty("createdAt", out var created)
                    && created.TryGetDateTimeOffset(out var createdAt))
                    return createdAt;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        var match = LegacyName().Match(directory.Name);
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var stamp)
            ? new DateTimeOffset(stamp)
            : null;
    }

    private static long MeasureSize(DirectoryInfo directory)
    {
        long total = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in current.EnumerateFiles())
                    total += file.Length;
                foreach (var child in current.EnumerateDirectories())
                {
                    if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Count what can be read; the size is a guide, not a guarantee.
            }
        }
        return total;
    }
}
