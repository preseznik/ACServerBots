using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Staging;
using ACEditor.Core.Validation;

namespace ACEditor.Core.Formats;

public sealed class AssettoCorsaTrackAdapter : ITrackFormatAdapter
{
    private readonly SafeStagingService _staging = new();
    public TrackFormat Format => TrackFormat.AssettoCorsa;

    public Task<TrackProbeResult> ProbeAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(sourcePath)) return new TrackProbeResult(Format, 0, "Assetto Corsa", [], []);
            var evidence = new List<string>();
            if (File.Exists(Path.Combine(sourcePath, "models.ini"))) evidence.Add("models.ini");
            evidence.AddRange(Directory.EnumerateFiles(sourcePath, "models_*.ini", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)!);
            if (Directory.EnumerateFiles(sourcePath, "*.kn5", SearchOption.TopDirectoryOnly).Any()) evidence.Add("KN5 render assets");
            if (Directory.Exists(Path.Combine(sourcePath, "ui"))) evidence.Add("ui metadata");
            return new TrackProbeResult(Format, Math.Min(100, evidence.Count * 25),
                Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath)), evidence, []);
        }, cancellationToken);

    public Task<TrackProject> ImportAsync(string sourcePath, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run(() => Import(sourcePath, progress, cancellationToken), cancellationToken);

    private static TrackProject Import(string sourcePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string root = PathRules.NormalizeDirectory(sourcePath);
        var project = new TrackProject
        {
            Name = Path.GetFileName(root), SourceRoot = root, SourceFormat = TrackFormat.AssettoCorsa,
            Coordinates = new CoordinateContract
            {
                Source = "Assetto Corsa native right-handed, Y-up, metres",
                Conversion = "identity; native values retained as provenance",
                ConversionAppliedExactlyOnce = true
            }
        };
        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[i];
            var info = new FileInfo(file);
            string relative = Path.GetRelativePath(root, file);
            project.SourceArtifacts.Add(new SourceArtifact
            {
                RelativePath = relative, Sha256 = ContentHash.Sha256(file), Length = info.Length,
                WriteDisposition = Classify(relative),
                BlockReason = Path.GetExtension(file).Equals(".kn5", StringComparison.OrdinalIgnoreCase)
                    ? "KN5 topology writes require the configured Blender exporter and reopen validation."
                    : null
            });
            progress?.Report((i + 1) * 0.35 / Math.Max(1, files.Length));
        }

        string[] modelFiles = Directory.EnumerateFiles(root, "models*.ini", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var loadedModels = new Dictionary<string, TrackNode>(StringComparer.OrdinalIgnoreCase);
        for (int modelIndex = 0; modelIndex < modelFiles.Length; modelIndex++)
        {
            string ownership = Path.GetFileNameWithoutExtension(modelFiles[modelIndex]) is "models"
                ? "shared" : Path.GetFileNameWithoutExtension(modelFiles[modelIndex])["models_".Length..];
            if (!project.LayoutIds.Contains(ownership, StringComparer.OrdinalIgnoreCase))
                project.LayoutIds.Add(ownership);
            foreach (string model in ReadModelFiles(modelFiles[modelIndex]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = PathRules.ResolveInside(root, model);
                if (!File.Exists(path)) throw new FileNotFoundException("A models INI references a missing KN5.", path);
                if (!loadedModels.TryGetValue(path, out TrackNode? node))
                {
                    node = Kn5Reader.Read(path, project.Scene, root);
                    node.Ownership = ownership;
                    loadedModels.Add(path, node);
                    project.Scene.Roots.Add(node);
                }
                else if (!node.Ownership.Split(',').Contains(ownership, StringComparer.OrdinalIgnoreCase))
                    node.Ownership += ", " + ownership;
            }
            progress?.Report(0.35 + (modelIndex + 1) * 0.45 / Math.Max(1, modelFiles.Length));
        }

        foreach (string aiFile in Directory.EnumerateFiles(root, "*_lane.ai", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetRelativePath(root, aiFile).Replace('\\', '/');
            project.Routes.Add(AssettoCorsaSplineReader.Read(aiFile, id));
        }
        progress?.Report(1);
        return project;
    }

    private static IEnumerable<string> ReadModelFiles(string path)
    {
        bool modelSection = false;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                modelSection = section.Equals("MODEL", StringComparison.OrdinalIgnoreCase) ||
                               section.StartsWith("MODEL_", StringComparison.OrdinalIgnoreCase) ||
                               section.StartsWith("DYNAMIC_OBJECT_", StringComparison.OrdinalIgnoreCase);
            }
            else if (modelSection && line.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                yield return line[5..].Trim();
        }
    }

    private static WriteDisposition Classify(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ini" or ".json" or ".ai" or ".png" or ".jpg" or ".dds" => WriteDisposition.RewriteKnown,
            ".kn5" => WriteDisposition.Blocked,
            _ => WriteDisposition.CopyUnchanged
        };
    }

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(TrackProject project,
        CancellationToken cancellationToken = default) => Task.FromResult(TrackValidator.Validate(project));
    public Task<StageResult> StageAsync(TrackProject project, StageOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        _staging.StageAsync(project, options, progress, cancellationToken);
}
