using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed record InstanceSummary(
    string RootPath,
    string PresetName,
    DateTimeOffset CreatedAt,
    string Track,
    int Slots,
    int BotSlots,
    bool IsCompactHistory = false)
{
    public string DisplayName => $"{(IsCompactHistory ? "History • " : string.Empty)}"
                                 + $"{CreatedAt.LocalDateTime:g} — {PresetName} ({Slots} slots, {Track})";
}

public sealed class InstanceCatalog(RaceControlPaths paths)
{
    public IReadOnlyList<InstanceSummary> List()
    {
        paths.EnsureCreated();
        var summaries = new List<InstanceSummary>();
        var manifestPaths = Directory.EnumerateFiles(paths.InstancesDirectory,
                "race-control-instance.json", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(paths.HistoryDirectory,
                "race-control-instance.json", SearchOption.AllDirectories));
        foreach (var manifestPath in manifestPaths)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                summaries.Add(new InstanceSummary(
                    Path.GetDirectoryName(manifestPath)!,
                    root.GetProperty("presetName").GetString() ?? "Race Control",
                    root.GetProperty("createdAt").GetDateTimeOffset(),
                    root.GetProperty("track").GetString() ?? string.Empty,
                    root.GetProperty("slots").GetInt32(),
                    root.GetProperty("botSlots").GetInt32(),
                    File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath)!,
                        "archive-info.json"))));
            }
            catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
            {
                // A partial or older instance remains on disk but is omitted from the UI.
            }
        }

        return summaries.OrderByDescending(summary => summary.CreatedAt).ToArray();
    }
}
