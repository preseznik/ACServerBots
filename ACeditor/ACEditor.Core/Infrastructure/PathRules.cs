namespace ACEditor.Core.Infrastructure;

public static class PathRules
{
    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A directory path is required.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string ResolveInside(string root, string relativePath)
    {
        string normalizedRoot = NormalizeDirectory(root);
        string resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes the source root: {relativePath}");
        return resolved;
    }

    public static bool IsInside(string parent, string candidate)
    {
        string normalizedParent = NormalizeDirectory(parent) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
