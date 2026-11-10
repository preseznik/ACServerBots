using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Staging;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class LegacyInstanceCatalogTests
{
    private string _root = null!;
    private RaceControlPaths _paths = null!;

    [SetUp]
    public void CreateDataRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"legacy-instances-{Guid.NewGuid():N}");
        _paths = new RaceControlPaths(_root);
        _paths.EnsureCreated();
    }

    [TearDown]
    public void DeleteDataRoot()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public void List_FindsOnlyOldTimestampedInstances()
    {
        string legacy = CreateFolder("Instances", "sunday-cup-1a2b3c4d-20250301-201500", ("AssettoServer.exe", 2048));
        string withManifest = CreateFolder("Instances", "renamed-by-hand", ("race-control-instance.json",
            0), ("cfg.ini", 100));
        File.WriteAllText(Path.Combine(withManifest, "race-control-instance.json"),
            """{ "createdAt": "2025-02-01T18:00:00+00:00" }""");
        CreateFolder("Instances", "Current", ("race-control-instance.json", 10));
        CreateFolder("Instances", "my-notes", ("readme.txt", 10));
        CreateFolder("History", "sunday-cup-1a2b3c4d-20250401-201500", ("race-control-instance.json", 10));

        var found = new LegacyInstanceCatalog(_paths).List();

        Assert.Multiple(() =>
        {
            Assert.That(found.Select(instance => instance.Name),
                Is.EqualTo(new[] { "renamed-by-hand", "sunday-cup-1a2b3c4d-20250301-201500" }), "oldest first");
            Assert.That(found.Single(instance => instance.Path == legacy).SizeBytes, Is.EqualTo(2048));
            Assert.That(found.Single(instance => instance.Path == legacy).CreatedAt?.LocalDateTime,
                Is.EqualTo(new DateTime(2025, 3, 1, 20, 15, 0)));
        });
    }

    [Test]
    public void CanRemove_RefusesCurrentHistoryAndOtherFolders()
    {
        string legacy = CreateFolder("Instances", "sunday-cup-1a2b3c4d-20250301-201500", ("a.txt", 1));
        string current = CreateFolder("Instances", "Current", ("race-control-instance.json", 1));
        string history = CreateFolder("History", "sunday-cup-1a2b3c4d-20250401-201500", ("race-control-instance.json", 1));
        string nested = CreateFolder(Path.Combine("Instances", "sunday-cup-1a2b3c4d-20250301-201500"),
            "inner-1a2b3c4d-20250301-201500", ("a.txt", 1));
        var catalog = new LegacyInstanceCatalog(_paths);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.CanRemove(legacy), Is.True);
            Assert.That(catalog.CanRemove(current), Is.False);
            Assert.That(catalog.CanRemove(history), Is.False);
            Assert.That(catalog.CanRemove(nested), Is.False, "only direct children of Instances");
            Assert.That(catalog.CanRemove(_paths.InstancesDirectory), Is.False);
            Assert.That(catalog.CanRemove(Path.Combine(_paths.InstancesDirectory, "missing-1a2b3c4d-20250301-201500")), Is.False);
        });
    }

    [Test]
    public void FormatSize_UsesReadableUnits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LegacyInstanceCatalog.FormatSize(512), Does.EndWith("bytes"));
            Assert.That(LegacyInstanceCatalog.FormatSize(3L << 20), Does.EndWith("MB"));
            Assert.That(LegacyInstanceCatalog.FormatSize(5L << 30), Does.EndWith("GB"));
        });
    }

    private string CreateFolder(string parent, string name, params (string File, int Size)[] files)
    {
        string directory = Path.Combine(_root, parent, name);
        Directory.CreateDirectory(directory);
        foreach (var (file, size) in files)
            File.WriteAllBytes(Path.Combine(directory, file), new byte[size]);
        return directory;
    }
}
