using ACEditor.Core.Models;

namespace ACEditor.Core.Formats;

internal static class AssettoCorsaSplineReader
{
    private const int MaxPoints = 2_000_000;

    public static TrackRoute Read(string path, string id)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        int version = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (count is < 2 or > MaxPoints) throw new InvalidDataException($"Invalid AI spline point count {count}.");
        var route = new TrackRoute { Id = id, DisplayName = Path.GetFileNameWithoutExtension(path) };
        if (version == 7)
        {
            reader.ReadInt32();
            reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                route.Points.Add(new RoutePoint
                {
                    Position = new Position3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                });
                reader.ReadSingle();
                reader.ReadInt32();
            }
            int extraCount = reader.ReadInt32();
            if (extraCount != count) throw new InvalidDataException("AI spline detail and extra counts differ.");
            for (int i = 0; i < count; i++)
            {
                RoutePoint point = route.Points[i];
                point.Speed = reader.ReadSingle();
                reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle();
                point.LeftWidth = reader.ReadSingle();
                point.RightWidth = reader.ReadSingle();
                for (int value = 0; value < 11; value++) reader.ReadSingle();
            }
        }
        else if (version == -1)
        {
            for (int i = 0; i < count; i++)
            {
                route.Points.Add(new RoutePoint
                {
                    Position = new Position3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    LeftWidth = 4, RightWidth = 4
                });
                reader.ReadSingle(); reader.ReadSingle();
            }
        }
        else throw new InvalidDataException($"Unsupported AI spline version {version}.");

        route.IsClosed = route.Points[0].Position.ToVector().Distance(route.Points[^1].Position.ToVector()) < 50;
        return route;
    }

    private static float Distance(this System.Numerics.Vector3 left, System.Numerics.Vector3 right) =>
        System.Numerics.Vector3.Distance(left, right);
}
