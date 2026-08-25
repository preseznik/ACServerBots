using System.IO.Compression;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed class InstanceStorageManager(RaceControlPaths paths)
{
    public async Task PrepareWorkingDirectoryAsync(IProgress<StagingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        string workingRoot = paths.WorkingInstanceDirectory;
        if (!Directory.Exists(workingRoot))
            return;

        string manifestPath = Path.Combine(workingRoot, "race-control-instance.json");
        if (File.Exists(manifestPath))
        {
            progress?.Report(new StagingProgress("Archive",
                "Preserving compact artifacts from the previous run…", 0));
            await ArchiveWorkingInstanceAsync(workingRoot, manifestPath, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(workingRoot, true);
    }

    private async Task ArchiveWorkingInstanceAsync(string workingRoot, string manifestPath,
        CancellationToken cancellationToken)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath,
            cancellationToken));
        JsonElement root = manifest.RootElement;
        string presetName = root.TryGetProperty("presetName", out var nameElement)
            ? nameElement.GetString() ?? "Race Control"
            : "Race Control";
        DateTimeOffset createdAt = root.TryGetProperty("createdAt", out var createdElement)
                                   && createdElement.TryGetDateTimeOffset(out var parsedCreatedAt)
            ? parsedCreatedAt
            : DateTimeOffset.Now;
        string shortId = root.TryGetProperty("presetId", out var idElement)
            ? (idElement.GetString() ?? string.Empty).Replace("-", string.Empty,
                StringComparison.Ordinal)
            : string.Empty;
        shortId = shortId.Length >= 8 ? shortId[..8] : "instance";

        string archiveName = $"{FileNameSanitizer.Slug(presetName)}-{shortId}-"
                             + createdAt.LocalDateTime.ToString("yyyyMMdd-HHmmss");
        string destination = GetUniqueDirectory(Path.Combine(paths.HistoryDirectory, archiveName));
        string temporary = Path.Combine(paths.HistoryDirectory, $".tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            await CopyFileAsync(manifestPath,
                Path.Combine(temporary, "race-control-instance.json"), cancellationToken);

            string presetRoot = Path.Combine(workingRoot, "presets", ServerInstanceStager.PresetName);
            if (Directory.Exists(presetRoot))
            {
                foreach (string source in Directory.EnumerateFiles(presetRoot, "*",
                             SearchOption.TopDirectoryOnly).Where(path =>
                             !path.EndsWith("race-physics.bin", StringComparison.OrdinalIgnoreCase)))
                {
                    await CopyRelativeFileAsync(workingRoot, source, temporary, cancellationToken);
                }
            }

            await CopyDirectoryAsync(workingRoot, Path.Combine(workingRoot, "logs"), temporary,
                cancellationToken);
            await CopyIfPresentAsync(workingRoot,
                Path.Combine(workingRoot, "simulation", "summary.json"), temporary,
                cancellationToken);
            await CopyIfPresentAsync(workingRoot,
                Path.Combine(workingRoot, "simulation", "events.jsonl"), temporary,
                cancellationToken);
            await CopyIfPresentAsync(workingRoot,
                Path.Combine(workingRoot, "race-control-live", "track.json"), temporary,
                cancellationToken);

            string samplesPath = Path.Combine(workingRoot, "simulation", "samples.jsonl");
            if (File.Exists(samplesPath))
            {
                string compressedPath = Path.Combine(temporary, "simulation", "samples.jsonl.gz");
                Directory.CreateDirectory(Path.GetDirectoryName(compressedPath)!);
                await using var input = new FileStream(samplesPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 131072, true);
                await using var output = new FileStream(compressedPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 131072, true);
                await using var gzip = new GZipStream(output, CompressionLevel.Optimal, true);
                await input.CopyToAsync(gzip, cancellationToken);
            }

            foreach (string rootLog in Directory.EnumerateFiles(workingRoot, "*.log",
                         SearchOption.TopDirectoryOnly))
            {
                await CopyRelativeFileAsync(workingRoot, rootLog, temporary, cancellationToken);
            }

            var archiveInfo = new
            {
                schemaVersion = 1,
                archivedAt = DateTimeOffset.Now,
                sourceCreatedAt = createdAt,
                storage = "compact-history",
                samplesCompression = File.Exists(samplesPath) ? "gzip" : null,
            };
            await File.WriteAllTextAsync(Path.Combine(temporary, "archive-info.json"),
                JsonSerializer.Serialize(archiveInfo,
                    new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Directory.Move(temporary, destination);
        }
        catch
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);
            throw;
        }
    }

    private static string GetUniqueDirectory(string requested)
    {
        if (!Directory.Exists(requested))
            return requested;
        for (int suffix = 2;; suffix++)
        {
            string candidate = $"{requested}-{suffix}";
            if (!Directory.Exists(candidate))
                return candidate;
        }
    }

    private static async Task CopyDirectoryAsync(string root, string sourceDirectory,
        string destinationRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
            return;
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*",
                     SearchOption.AllDirectories))
        {
            await CopyRelativeFileAsync(root, source, destinationRoot, cancellationToken);
        }
    }

    private static Task CopyIfPresentAsync(string root, string source, string destinationRoot,
        CancellationToken cancellationToken) => File.Exists(source)
        ? CopyRelativeFileAsync(root, source, destinationRoot, cancellationToken)
        : Task.CompletedTask;

    private static Task CopyRelativeFileAsync(string root, string source, string destinationRoot,
        CancellationToken cancellationToken) => CopyFileAsync(source,
        Path.Combine(destinationRoot, Path.GetRelativePath(root, source)), cancellationToken);

    private static async Task CopyFileAsync(string source, string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 131072, true);
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 131072, true);
        await input.CopyToAsync(output, cancellationToken);
    }
}

public sealed class InstancePackageExporter
{
    public async Task ExportAsync(string instanceRoot, string destinationZip,
        CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(instanceRoot);
        string destination = Path.GetFullPath(destinationZip);
        if (!Directory.Exists(source)
            || !File.Exists(Path.Combine(source, "race-control-instance.json")))
            throw new InvalidOperationException("No complete staged server instance is available to export.");
        if (destination.StartsWith(source + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export the package outside the working server directory.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 131072, true))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (string file in Directory.EnumerateFiles(source, "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entryName = Path.GetRelativePath(source, file)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 131072, true);
                    await using var output = entry.Open();
                    await input.CopyToAsync(output, cancellationToken);
                }
            }

            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
