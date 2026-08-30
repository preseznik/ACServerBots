using System.Text.Json;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Validation;

namespace ACEditor.Core.Staging;

public sealed class SafeStagingService
{
    private const string ManifestFileName = ".aceditor-stage.json";

    public Task<StageResult> StageAsync(TrackProject project, StageOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        Action<string, CancellationToken>? prepareStagedCopy = null) =>
        Task.Run(() => Stage(project, options, progress, cancellationToken, prepareStagedCopy), cancellationToken);

    private static StageResult Stage(TrackProject project, StageOptions options,
        IProgress<double>? progress, CancellationToken cancellationToken,
        Action<string, CancellationToken>? prepareStagedCopy)
    {
        var issues = TrackValidator.Validate(project).ToList();
        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            return new StageResult { OutputDirectory = options.OutputDirectory, Issues = issues };

        string sourceRoot = PathRules.NormalizeDirectory(project.SourceRoot);
        string outputRoot = PathRules.NormalizeDirectory(options.OutputDirectory);
        if (outputRoot.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
            PathRules.IsInside(sourceRoot, outputRoot) || PathRules.IsInside(outputRoot, sourceRoot))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "UNSAFE_STAGE_PATH",
                "The staging directory must be separate from the source tree.", outputRoot));
            return new StageResult { OutputDirectory = outputRoot, Issues = issues };
        }

        string parent = Path.GetDirectoryName(outputRoot)!;
        Directory.CreateDirectory(parent);
        string temporaryRoot = Path.Combine(parent, $".{Path.GetFileName(outputRoot)}.tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            int completed = 0;
            foreach (SourceArtifact artifact in project.SourceArtifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string source = PathRules.ResolveInside(sourceRoot, artifact.RelativePath);
                string destination = PathRules.ResolveInside(temporaryRoot, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                if (!string.Equals(ContentHash.Sha256(destination), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Copied artifact hash mismatch: {artifact.RelativePath}");
                progress?.Report(++completed / (double)Math.Max(1, project.SourceArtifacts.Count));
            }

            prepareStagedCopy?.Invoke(temporaryRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            List<SourceArtifact> stagedManifest = project.SourceArtifacts.Select(item =>
            {
                string stagedPath = PathRules.ResolveInside(temporaryRoot, item.RelativePath);
                return Clone(item, ContentHash.Sha256(stagedPath), new FileInfo(stagedPath).Length);
            }).ToList();

            File.WriteAllText(Path.Combine(temporaryRoot, ManifestFileName), JsonSerializer.Serialize(new
            {
                project.ProjectId,
                project.SourceFormat,
                SourceRoot = sourceRoot,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Artifacts = stagedManifest.Select(item => new
                    { item.RelativePath, item.Sha256, item.Length, item.WriteDisposition })
            }, new JsonSerializerOptions { WriteIndented = true }));

            if (Directory.Exists(outputRoot))
            {
                if (!options.OverwriteExisting || !File.Exists(Path.Combine(outputRoot, ManifestFileName)))
                    throw new IOException("The staging directory already exists and is not a replaceable AC Editor stage.");
                string backup = outputRoot + ".replaced-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(outputRoot, backup);
            }
            Directory.Move(temporaryRoot, outputRoot);
            return new StageResult
            {
                OutputDirectory = outputRoot,
                Manifest = stagedManifest,
                Issues = issues
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "STAGE_FAILED", exception.Message, outputRoot));
            return new StageResult { OutputDirectory = outputRoot, Issues = issues };
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static SourceArtifact Clone(SourceArtifact item, string sha256, long length) => new()
    {
        RelativePath = item.RelativePath, Sha256 = sha256, Length = length,
        WriteDisposition = item.WriteDisposition, BlockReason = item.BlockReason
    };
}
