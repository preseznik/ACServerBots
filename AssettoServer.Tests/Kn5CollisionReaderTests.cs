using System.Numerics;
using System.Text;
using AssettoServer.Server.Ai.Physics;

namespace AssettoServer.Tests;

[TestFixture]
public class Kn5CollisionReaderTests
{
    [TestCase(5, "none")]
    [TestCase(6, "none")]
    [TestCase(5, "active")]
    [TestCase(6, "active")]
    [TestCase(6, "inactive")]
    [TestCase(5, "placeholder")]
    [TestCase(6, "placeholder")]
    [TestCase(6, "placeholder-active")]
    [TestCase(6, "placeholder-inactive")]
    public void TextureLayoutsPreserveColliderGeometryAndVisualTransforms(int version, string layout)
    {
        string path = Path.Combine(Path.GetTempPath(), $"kn5-textures-{Guid.NewGuid():N}.kn5");
        try
        {
            WriteCollider(path, version, layout);
            var collider = Kn5CollisionReader.Read(path, _ => true);
            var visual = Kn5CollisionReader.Read(path, _ => false, includeTriangles: false);

            Assert.Multiple(() =>
            {
                Assert.That(collider.Vertices, Is.EqualTo(new[]
                {
                    new Vector3(2, 3, 4), new Vector3(3, 3, 4), new Vector3(2, 3, 5)
                }));
                Assert.That(collider.Triangles, Has.Count.EqualTo(1));
                Assert.That(collider.MeshNames, Is.EquivalentTo(new[] { "COLLIDER" }));
                Assert.That(visual.NamedTransforms, Is.EqualTo(collider.NamedTransforms));
                Assert.That(visual.NamedTransforms.Single().Name, Is.EqualTo("ROOT"));
                Assert.That(visual.NamedTransforms.Single().Transform.Translation, Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(visual.Vertices, Is.Empty);
                Assert.That(visual.Triangles, Is.Empty);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void MalformedPlaceholderFileStillFailsAndRetainsOriginalDiagnostic()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kn5-truncated-{Guid.NewGuid():N}.kn5");
        try
        {
            WriteCollider(path, 6, "placeholder-active");
            using (var file = File.OpenWrite(path))
                file.SetLength(file.Length - 1);

            var exception = Assert.Throws<InvalidDataException>(() => Kn5CollisionReader.Read(path, _ => true));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain(path));
                Assert.That(exception.Message, Does.Contain("during texture 0"));
                Assert.That(exception.InnerException, Is.TypeOf<EndOfStreamException>());
                Assert.That(exception.InnerException!.StackTrace, Does.Contain("Kn5CollisionReader.Skip"));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteCollider(string path, int version, string layout)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("sc6969"));
        writer.Write(version);
        if (version > 5) writer.Write(0);

        bool placeholder = layout.StartsWith("placeholder");
        bool embedded = layout is "active" or "inactive" or "placeholder-active" or "placeholder-inactive";
        writer.Write((placeholder ? 1 : 0) + (embedded ? 1 : 0));
        if (placeholder) writer.Write(0); // The entire placeholder record is one zero word.
        if (embedded)
        {
            writer.Write(layout.EndsWith("inactive") ? 0 : 1);
            WriteString(writer, "body.dds");
            writer.Write(3);
            writer.Write(new byte[] { 1, 2, 3 });
        }

        writer.Write(1); // Material count.
        WriteString(writer, "collider");
        WriteString(writer, "ksPerPixel");
        writer.Write(new byte[6]); // Blend, alpha test, depth mode.
        writer.Write(0); // Properties.
        writer.Write(0); // Texture mappings.

        writer.Write(1); // Root transform node.
        WriteString(writer, "ROOT");
        writer.Write(1); // Children.
        writer.Write(true);
        foreach (float value in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 2, 3, 4, 1 })
            writer.Write(value);

        writer.Write(2); // Mesh node.
        WriteString(writer, "COLLIDER");
        writer.Write(0); // Children.
        writer.Write(true);
        writer.Write(new byte[] { 1, 1, 0 }); // Shadows, visibility, transparency.
        writer.Write(3); // Vertices.
        foreach (var vertex in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitZ })
        {
            writer.Write(vertex.X);
            writer.Write(vertex.Y);
            writer.Write(vertex.Z);
            writer.Write(new byte[32]); // Normal, UV, tangent.
        }
        writer.Write(3); // Indices.
        foreach (ushort index in new ushort[] { 0, 1, 2 }) writer.Write(index);
        writer.Write(new byte[32]); // Material, layer, LOD distances, bounding sphere.
        writer.Write(true); // Renderable.
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
