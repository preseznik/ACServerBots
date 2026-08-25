using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Storage;

public sealed class FpsArenaStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly RaceControlPaths _paths;

    public FpsArenaStore(RaceControlPaths paths) => _paths = paths;

    public FpsArenaDefinition? Load(string trackId, string layoutId)
    {
        var path = GetPath(trackId, layoutId);
        if (!File.Exists(path)) return null;

        return JsonSerializer.Deserialize<FpsArenaDefinition>(File.ReadAllText(path), JsonOptions);
    }

    public string Save(FpsArenaDefinition arena)
    {
        ArgumentNullException.ThrowIfNull(arena);
        _paths.EnsureCreated();
        var path = GetPath(arena.TrackId, arena.LayoutId);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(arena, JsonOptions));
        File.Move(temporary, path, true);
        return path;
    }

    public bool IsPrepared(string trackId, string layoutId)
    {
        try
        {
            var arena = Load(trackId, layoutId);
            return arena is
            {
                PreparationVersion: FpsArenaDefinition.CurrentPreparationVersion,
                SpawnPoints.Count: >= 2,
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }

    private string GetPath(string trackId, string layoutId)
    {
        var key = string.IsNullOrWhiteSpace(layoutId) ? trackId : $"{trackId}-{layoutId}";
        return Path.Combine(_paths.FpsArenasDirectory, FileNameSanitizer.Slug(key) + ".json");
    }
}
