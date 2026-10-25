using System.Diagnostics;
using System.Text;
using AssettoServer.Server.Ai.Physics;

namespace AssettoServer.Tests;

[TestFixture]
public class PhysicsPreparationDiagnosticsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"physics-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [TestCase("header")]
    [TestCase("texture 0 'body.dds' data")]
    [TestCase("material 'body' properties")]
    [TestCase("node 'ROOT' transform")]
    [TestCase("child 0 header of node 'ROOT'")]
    [TestCase("mesh 'BODY' vertices")]
    public void TruncatedKn5ReportsFileStageOffsetAndOriginalStack(string stage)
    {
        string path = Path.Combine(_root, "broken.kn5");
        WriteTruncatedKn5(path, stage);

        var exception = Assert.Throws<InvalidDataException>(() =>
            Kn5CollisionReader.Read(path, _ => true));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(path));
            Assert.That(exception.Message, Does.Contain($"during {stage} at byte "));
            Assert.That(exception.Message, Does.Contain($"of {new FileInfo(path).Length}"));
            Assert.That(exception.InnerException, Is.TypeOf<EndOfStreamException>());
            Assert.That(exception.ToString(), Does.Contain("Kn5CollisionReader.Read"));
            Assert.That(exception.InnerException!.StackTrace, Is.Not.Empty);
        });
    }

    [Test]
    public async Task PreparationProcessReportsFullExceptionChainAndKeepsExitCodeTwo()
    {
        string trackRoot = Path.Combine(_root, "content", "tracks", "test_track");
        Directory.CreateDirectory(Path.Combine(trackRoot, "ai"));
        using (var writer = new BinaryWriter(File.Create(Path.Combine(trackRoot, "ai", "fast_lane.ai"))))
        {
            writer.Write(-1);
            writer.Write(32);
            for (int i = 0; i < 32; i++)
            {
                double angle = i * Math.PI * 2 / 31;
                writer.Write((float)(Math.Cos(angle) * 100));
                writer.Write(0f);
                writer.Write((float)(Math.Sin(angle) * 100));
                writer.Write(100f);
                writer.Write(0f);
            }
        }
        string modelPath = Path.Combine(trackRoot, "track.kn5");
        WriteTruncatedKn5(modelPath, "texture 0 'body.dds' data");
        await File.WriteAllTextAsync(Path.Combine(trackRoot, "models.ini"), "[MODEL_0]\nFILE=track.kn5\n");
        string output = Path.Combine(_root, "race-physics.bin");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string testAssembly = typeof(PhysicsPreparationDiagnosticsTests).Assembly.Location;
        foreach (string argument in new[] { "exec", "--runtimeconfig",
                     Path.ChangeExtension(testAssembly, "runtimeconfig.json"), "--depsfile",
                     Path.ChangeExtension(testAssembly, "deps.json"), typeof(Program).Assembly.Location,
                     "--prepare-race-physics",
                     "--ac-root", _root, "--track", "test_track", "--cars", "test_car",
                     "--physics-output", output })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        await stdout;
        string diagnostic = await stderr;

        Assert.Multiple(() =>
        {
            Assert.That(process.ExitCode, Is.EqualTo(2), diagnostic);
            Assert.That(diagnostic, Does.Contain(modelPath));
            Assert.That(diagnostic, Does.Contain("during texture 0 'body.dds' data"));
            Assert.That(diagnostic, Does.Contain("System.IO.EndOfStreamException"));
            Assert.That(diagnostic, Does.Contain("Kn5CollisionReader.Skip"));
            Assert.That(diagnostic, Does.Contain("RacePhysicsAssetBuilder.Build"));
            Assert.That(diagnostic, Does.Contain("Program.PrepareRacePhysics"));
            Assert.That(File.Exists(output), Is.False);
        });
    }

    private static void WriteTruncatedKn5(string path, string stage)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("sc6969"));
        if (stage == "header") return;
        writer.Write(5);
        writer.Write(stage.StartsWith("texture") ? 1 : 0);
        if (stage.StartsWith("texture"))
        {
            writer.Write(0);
            WriteString(writer, "body.dds");
            writer.Write(100u); // Payload is missing.
            return;
        }
        writer.Write(stage.StartsWith("material") ? 1 : 0);
        if (stage.StartsWith("material"))
        {
            WriteString(writer, "body");
            WriteString(writer, "ksPerPixel");
            writer.Write(new byte[6]); // Property count is missing.
            return;
        }
        bool mesh = stage.StartsWith("mesh");
        writer.Write(mesh ? 2 : 1);
        WriteString(writer, mesh ? "BODY" : "ROOT");
        writer.Write(stage.StartsWith("child") ? 1 : 0);
        writer.Write(true);
        if (mesh)
        {
            writer.Write(new byte[3]); // Flags; vertex count is missing.
        }
        else if (stage.StartsWith("child"))
        {
            writer.Write(new byte[16 * sizeof(float)]); // Child header is missing.
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
