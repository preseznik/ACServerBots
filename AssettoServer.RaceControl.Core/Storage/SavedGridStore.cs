using System.Text.Json;
using System.Text.Json.Serialization;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Storage;

public sealed class SavedGridStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RaceControlPaths _paths;

    public SavedGridStore(RaceControlPaths paths) => _paths = paths;

    public IReadOnlyList<SavedGridSummary> List()
    {
        _paths.EnsureCreated();
        return Directory.EnumerateFiles(_paths.GridsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                try
                {
                    var grid = Load(path);
                    return new SavedGridSummary(grid.Id, grid.Name, path, grid.UpdatedAt);
                }
                catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or JsonException
                                                  or InvalidDataException)
                {
                    return null;
                }
            })
            .OfType<SavedGridSummary>()
            .OrderByDescending(summary => summary.UpdatedAt)
            .ToArray();
    }

    public SavedGridPreset Load(string path)
    {
        var json = File.ReadAllText(path);
        var grid = JsonSerializer.Deserialize<SavedGridPreset>(json, JsonOptions)
                   ?? throw new InvalidDataException($"Saved grid is empty: {path}");
        if (grid.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported saved-grid schema {grid.SchemaVersion}: {path}");
        return grid;
    }

    public string Save(SavedGridPreset grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (string.IsNullOrWhiteSpace(grid.Name))
            throw new ArgumentException("Grid name is required.", nameof(grid));
        if (grid.Slots.Count == 0)
            throw new ArgumentException("A saved grid must contain at least one slot.", nameof(grid));

        _paths.EnsureCreated();
        grid.Name = grid.Name.Trim();
        grid.UpdatedAt = DateTimeOffset.Now;
        string path = Path.Combine(_paths.GridsDirectory,
            $"{FileNameSanitizer.Slug(grid.Name)}-{grid.Id:N}.json");
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(grid, JsonOptions));
            File.Move(temporary, path, true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void Delete(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_paths.GridsDirectory))
                      + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Saved grid is outside the Race Control grid directory.");
        File.Delete(fullPath);
    }
}
