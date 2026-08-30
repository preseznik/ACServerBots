using ACEditor.Core.Infrastructure;

namespace ACEditor.Core.Staging;

public sealed record PublishResult(string InstalledPath, string? BackupPath);

public sealed class SafePublisher
{
    public Task<PublishResult> PublishAsync(string stagedDirectory, string targetDirectory,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        string stage = PathRules.NormalizeDirectory(stagedDirectory);
        string target = PathRules.NormalizeDirectory(targetDirectory);
        if (!File.Exists(Path.Combine(stage, ".aceditor-stage.json")))
            throw new InvalidDataException("The selected source is not an AC Editor staged copy.");
        if (stage.Equals(target, StringComparison.OrdinalIgnoreCase) || PathRules.IsInside(stage, target) ||
            PathRules.IsInside(target, stage))
            throw new InvalidDataException("The publish target must be separate from the staging tree.");

        string parent = Path.GetDirectoryName(target)!;
        string temporary = Path.Combine(parent, $".{Path.GetFileName(target)}.publish-{Guid.NewGuid():N}");
        CopyDirectory(stage, temporary, cancellationToken);
        string? backup = null;
        try
        {
            if (Directory.Exists(target))
            {
                backup = target + ".aceditor-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(target, backup);
            }
            Directory.Move(temporary, target);
            return new PublishResult(target, backup);
        }
        catch
        {
            if (backup is not null && Directory.Exists(backup) && !Directory.Exists(target))
                Directory.Move(backup, target);
            throw;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }, cancellationToken);

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, file);
            File.Copy(file, PathRules.ResolveInside(destination, relative));
        }
    }
}
