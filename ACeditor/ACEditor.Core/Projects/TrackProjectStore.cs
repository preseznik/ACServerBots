using System.Text.Json;
using System.Text.Json.Serialization;
using ACEditor.Core.Models;

namespace ACEditor.Core.Projects;

public sealed class TrackProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(TrackProject project, string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        project.ProjectFile = fullPath;
        project.ModifiedAtUtc = DateTimeOffset.UtcNow;
        string temporaryPath = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<TrackProject> LoadAsync(string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        await using var stream = File.OpenRead(fullPath);
        TrackProject project = await JsonSerializer.DeserializeAsync<TrackProject>(
                                   stream, JsonOptions, cancellationToken)
                               ?? throw new InvalidDataException("The project file is empty.");
        if (project.SchemaVersion != TrackProject.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported .acedit schema {project.SchemaVersion}; expected {TrackProject.CurrentSchemaVersion}.");
        project.ProjectFile = fullPath;
        return project;
    }
}
