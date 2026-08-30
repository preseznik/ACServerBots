using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;

namespace ACEditor.Core.Validation;

public static class TrackValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(TrackProject project)
    {
        var issues = new List<ValidationIssue>();
        if (!Directory.Exists(project.SourceRoot))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "SOURCE_MISSING",
                "The imported source directory no longer exists.", project.SourceRoot));
            return issues;
        }

        foreach (SourceArtifact artifact in project.SourceArtifacts)
        {
            string path;
            try { path = PathRules.ResolveInside(project.SourceRoot, artifact.RelativePath); }
            catch (InvalidDataException exception)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "ARTIFACT_PATH_ESCAPE",
                    exception.Message, artifact.RelativePath));
                continue;
            }
            if (!File.Exists(path))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "ARTIFACT_MISSING",
                    "A source artifact is missing.", artifact.RelativePath));
                continue;
            }
            if (!string.Equals(ContentHash.Sha256(path), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "SOURCE_CHANGED",
                    "The source changed after import; reimport before staging.", artifact.RelativePath));
        }

        foreach (TrackEditDelta edit in project.EditDeltas)
        {
            if (string.IsNullOrWhiteSpace(edit.RequiredArtifact)) continue;
            SourceArtifact? artifact = project.SourceArtifacts.FirstOrDefault(item =>
                item.RelativePath.Equals(edit.RequiredArtifact, StringComparison.OrdinalIgnoreCase));
            if (artifact?.WriteDisposition == WriteDisposition.Blocked)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "OPAQUE_WRITE_BLOCKED",
                    artifact.BlockReason ?? "This edit requires rewriting unsupported source data.",
                    artifact.RelativePath, edit.TargetId));
        }

        if (project.Scene.Roots.Count == 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EMPTY_SCENE",
                "No renderable scene nodes were imported."));
        if (project.Routes.Count == 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NO_ROUTE",
                "No route or AI line was imported."));
        return issues;
    }
}
