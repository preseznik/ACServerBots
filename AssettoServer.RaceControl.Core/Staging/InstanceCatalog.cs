using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed record InstanceSummary(
    string RootPath,
    string PresetName,
    DateTimeOffset CreatedAt,
    string Track,
    int Slots,
    int BotSlots)
{
    public string DisplayName => $"{CreatedAt.LocalDateTime:g} — {PresetName} ({Slots} slots, {Track})";
}

public sealed class InstanceCatalog(RaceControlPaths paths)
{
    public IReadOnlyList<InstanceSummary> List()
    {
        paths.EnsureCreated();
        var summaries = new List<InstanceSummary>();
        foreach (var manifestPath in Directory.EnumerateFiles(paths.InstancesDirectory, "race-control-instance.json", SearchOption.AllDirectories))
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
                    root.GetProperty("botSlots").GetInt32()));
            }
            catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
            {
                // A partial or older instance remains on disk but is omitted from the UI.
            }
        }

        return summaries.OrderByDescending(summary => summary.CreatedAt).ToArray();
    }
}
