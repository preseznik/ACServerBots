using System.Numerics;
using System.Text;
using ACEditor.Core.Models;

namespace ACEditor.Tests;

internal static class SyntheticFixtures
{
    public static void WriteMinimalKn5(string path, bool truncate = false, bool meshVisible = true,
        bool meshRenderable = true)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(Encoding.ASCII.GetBytes("sc6969"));
        writer.Write(5);
        byte[] texture = CreateDxt1Dds();
        writer.Write(1); // textures
        writer.Write(0); WriteString(writer, "road.dds"); writer.Write((uint)texture.Length); writer.Write(texture);
        writer.Write(1); // materials
        WriteString(writer, "road");
        WriteString(writer, "ksPerPixel");
        writer.Write((byte)MaterialBlendMode.AlphaToCoverage);
        writer.Write(true);
        writer.Write((int)MaterialDepthMode.NoWrite);
        writer.Write(0);
        writer.Write(1); WriteString(writer, "txDiffuse"); writer.Write(0); WriteString(writer, "road.dds");
        writer.Write(1); WriteString(writer, "Root"); writer.Write(1); writer.Write(true);
        WriteMatrix(writer, Matrix4x4.Identity);
        writer.Write(2); WriteString(writer, "1ROAD"); writer.Write(0); writer.Write(true);
        writer.Write(true); writer.Write(meshVisible); writer.Write(false);
        writer.Write((uint)3);
        WriteVertex(writer, new Vector3(-2, 0, 0));
        WriteVertex(writer, new Vector3(2, 0, 0));
        WriteVertex(writer, new Vector3(0, 0, 5));
        writer.Write((uint)3); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2);
        writer.Write((uint)0); writer.Write((uint)0); writer.Write(0f); writer.Write(10_000f);
        writer.Write(0f); writer.Write(0f); writer.Write(2.5f); writer.Write(3f); writer.Write(meshRenderable);
        if (truncate) stream.SetLength(Math.Max(8, stream.Length - 19));
    }

    public static byte[] CreateDxt1Dds()
    {
        byte[] bytes = new byte[136];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124u);
        writer.Write(0x000A1007u);
        writer.Write(4u); writer.Write(4u); writer.Write(8u); writer.Write(0u); writer.Write(1u);
        for (int index = 0; index < 11; index++) writer.Write(0u);
        writer.Write(32u); writer.Write(4u); writer.Write(Encoding.ASCII.GetBytes("DXT1"));
        writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
        writer.Write(0x1000u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
        writer.Write((ushort)0xf800); writer.Write((ushort)0x07e0); writer.Write(0u);
        return bytes;
    }

    public static void WriteV7Spline(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(7); writer.Write(3); writer.Write(60_000); writer.Write(3);
        Vector3[] points = [new(0, 0, 0), new(0, 0, 10), new(0.2f, 0, 0.1f)];
        for (int i = 0; i < points.Length; i++)
        {
            writer.Write(points[i].X); writer.Write(points[i].Y); writer.Write(points[i].Z);
            writer.Write(i * 10f); writer.Write(i);
        }
        writer.Write(points.Length);
        foreach (Vector3 _ in points)
        {
            writer.Write(40f); writer.Write(1f); writer.Write(0f); writer.Write(0f); writer.Write(50f);
            writer.Write(4f); writer.Write(5f); writer.Write(0f); writer.Write(1f);
            writer.Write(0f); writer.Write(1f); writer.Write(0f); writer.Write(10f);
            writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(0f); writer.Write(0f);
        }
    }

    public static string CreateAssettoCorsaTrack(string root)
    {
        Directory.CreateDirectory(root);
        WriteMinimalKn5(System.IO.Path.Combine(root, "track.kn5"));
        File.WriteAllText(System.IO.Path.Combine(root, "models.ini"), "[MODEL_0]\nFILE=track.kn5\nPOSITION=0,0,0\nROTATION=0,0,0\n");
        WriteV7Spline(System.IO.Path.Combine(root, "ai", "fast_lane.ai"));
        File.WriteAllBytes(System.IO.Path.Combine(root, "opaque.dat"), [0, 1, 2, 3, 255, 9]);
        return root;
    }

    private static void WriteVertex(BinaryWriter writer, Vector3 position)
    {
        writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
        writer.Write(0f); writer.Write(1f); writer.Write(0f);
        writer.Write(0f); writer.Write(0f);
        writer.Write(1f); writer.Write(0f); writer.Write(0f);
    }
    private static void WriteMatrix(BinaryWriter writer, Matrix4x4 matrix)
    {
        writer.Write(matrix.M11); writer.Write(matrix.M12); writer.Write(matrix.M13); writer.Write(matrix.M14);
        writer.Write(matrix.M21); writer.Write(matrix.M22); writer.Write(matrix.M23); writer.Write(matrix.M24);
        writer.Write(matrix.M31); writer.Write(matrix.M32); writer.Write(matrix.M33); writer.Write(matrix.M34);
        writer.Write(matrix.M41); writer.Write(matrix.M42); writer.Write(matrix.M43); writer.Write(matrix.M44);
    }
    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
}
