namespace ACEditor.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ACEditor.Tests", Guid.NewGuid().ToString("N"));
    public string Path { get; }
    public string Create()
    {
        Directory.CreateDirectory(Path);
        return Path;
    }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
