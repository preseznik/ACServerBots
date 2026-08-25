using System.Numerics;
using AssettoServer.RaceControl.Core.Models;

namespace AssettoServer.RaceControl.Core.Content;

internal static class RaceBotTrackPreflightInspector
{
    private const int MinimumSplinePoints = 20;
    private const int MaximumSplinePoints = 2_000_000;
    private const double MaximumClosureDistanceMeters = 50;

    public static RaceBotTrackPreflight Inspect(string modelsIniPath, string trackRoot,
        string fastLanePath)
    {
        var missingModels = InspectModelFiles(modelsIniPath, trackRoot);
        try
        {
            var (pointCount, closureDistance) = ReadSplineSummary(fastLanePath);
            bool closed = closureDistance <= MaximumClosureDistanceMeters;
            return new RaceBotTrackPreflight(closed, pointCount, closureDistance,
                missingModels, closed ? null : $"fast_lane.ai is open ({closureDistance:F1} m endpoint gap)");
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException)
        {
            return new RaceBotTrackPreflight(false, 0, null, missingModels,
                exception.Message);
        }
    }

    internal static IReadOnlyList<string> InspectModelFiles(string modelsIniPath, string trackRoot)
    {
        if (!File.Exists(modelsIniPath))
            return [Path.GetFileName(modelsIniPath)];

        var missing = new List<string>();
        bool isTrackModelSection = true;
        foreach (string rawLine in File.ReadLines(modelsIniPath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                isTrackModelSection = section.Equals("MODEL", StringComparison.OrdinalIgnoreCase)
                                      || section.StartsWith("MODEL_", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!isTrackModelSection || !line.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                continue;

            string relative = CleanIniValue(line[5..]);
            if (string.IsNullOrWhiteSpace(relative))
                continue;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(trackRoot, relative));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                missing.Add(relative);
                continue;
            }
            if (!fullPath.StartsWith(Path.GetFullPath(trackRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                missing.Add(relative);
        }
        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static (int PointCount, double ClosureDistanceMeters) ReadSplineSummary(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Selected track layout has no fast_lane.ai", path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        int version = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (count is < MinimumSplinePoints or > MaximumSplinePoints)
            throw new InvalidDataException($"Race spline has an invalid point count: {count}");

        long firstPositionOffset;
        long pointStride;
        long minimumLength;
        switch (version)
        {
            case -1:
                firstPositionOffset = 8;
                pointStride = 5 * sizeof(float);
                minimumLength = checked(firstPositionOffset + count * pointStride);
                break;
            case 7:
                firstPositionOffset = 4 * sizeof(int);
                pointStride = 3 * sizeof(float) + sizeof(float) + sizeof(int);
                minimumLength = checked(firstPositionOffset + count * pointStride
                                        + sizeof(int) + count * 18L * sizeof(float));
                break;
            default:
                throw new InvalidDataException($"Unsupported fast_lane.ai version {version}: {path}");
        }
        if (stream.Length < minimumLength)
            throw new InvalidDataException($"fast_lane.ai is truncated: {path}");

        stream.Position = firstPositionOffset;
        Vector3 first = ReadVector3(reader);
        stream.Position = firstPositionOffset + (count - 1L) * pointStride;
        Vector3 last = ReadVector3(reader);
        if (!IsFinite(first) || !IsFinite(last))
            throw new InvalidDataException($"fast_lane.ai contains invalid coordinates: {path}");
        return (count, Vector3.Distance(first, last));
    }

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string CleanIniValue(string value) => value.Split(';', 2)[0].Trim();
}
