using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace AssettoServer.Server.Ai.Physics;

internal readonly record struct RaceCarPhysicsCalibration(
    float FrontTyreRadius,
    float RearTyreRadius,
    Vector3 GraphicsOffset,
    string Source)
{
    public bool IsAuthoritative => FrontTyreRadius > 0 && RearTyreRadius > 0;
}

/// <summary>
/// Reads the small subset of AC car data required by the server-side wheel model. Unpacked
/// data takes precedence, matching Assetto Corsa. Packed data.acd entries are read in place;
/// no installed game files are extracted or modified.
/// </summary>
internal static class AcCarPhysicsReader
{
    private const int MaximumEntryNameBytes = 4096;
    private const int MaximumEntryDataBytes = 64 * 1024 * 1024;

    public static RaceCarPhysicsCalibration Read(string carRoot, string model)
    {
        string dataRoot = Path.Combine(carRoot, "data");
        string? tyres = ReadUnpacked(dataRoot, "tyres.ini");
        string? car = ReadUnpacked(dataRoot, "car.ini");
        string source = "unpacked data";
        bool packedDataUnreadable = false;

        if (tyres is null || car is null)
        {
            string acdPath = Path.Combine(carRoot, "data.acd");
            if (File.Exists(acdPath))
            {
                try
                {
                    var packed = ReadPackedEntries(acdPath, model, ["tyres.ini", "car.ini"]);
                    tyres ??= packed.GetValueOrDefault("tyres.ini");
                    car ??= packed.GetValueOrDefault("car.ini");
                    source = Directory.Exists(dataRoot) ? "unpacked data + data.acd" : "data.acd";
                }
                catch (InvalidDataException)
                {
                    packedDataUnreadable = true;
                }
            }
        }

        if (tyres is null)
            return new RaceCarPhysicsCalibration(0, 0, ReadGraphicsOffset(car),
                packedDataUnreadable ? "visual wheel fallback (unreadable data.acd)" : "visual wheel fallback");

        var ini = ParseIni(tyres);
        float front = ReadRequiredFloat(ini, "FRONT", "RADIUS", model);
        float rear = ReadRequiredFloat(ini, "REAR", "RADIUS", model);
        if (front is < 0.15f or > 0.8f || rear is < 0.15f or > 0.8f)
            throw new InvalidDataException($"Car {model} has implausible tyre radii: front {front:F3} m, rear {rear:F3} m");

        return new RaceCarPhysicsCalibration(front, rear, ReadGraphicsOffset(car), source);
    }

    private static string? ReadUnpacked(string dataRoot, string name)
    {
        string path = Path.Combine(dataRoot, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static Vector3 ReadGraphicsOffset(string? carIni)
    {
        if (carIni is null)
            return Vector3.Zero;
        var ini = ParseIni(carIni);
        if (!ini.TryGetValue(("BASIC", "GRAPHICS_OFFSET"), out string? value))
            return Vector3.Zero;
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
            || Math.Abs(x) > 3 || Math.Abs(y) > 3 || Math.Abs(z) > 3)
        {
            throw new InvalidDataException($"Invalid BASIC/GRAPHICS_OFFSET in car.ini: {value}");
        }
        return new Vector3(x, y, z);
    }

    private static float ReadRequiredFloat(Dictionary<(string Section, string Key), string> ini,
        string section, string key, string model)
    {
        if (!ini.TryGetValue((section, key), out string? value)
            || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            || !float.IsFinite(result))
        {
            throw new InvalidDataException($"Car {model} has no usable [{section}] {key} in tyres.ini");
        }
        return result;
    }

    internal static Dictionary<(string Section, string Key), string> ParseIni(string text)
    {
        var result = new Dictionary<(string, string), string>();
        string section = string.Empty;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            int comment = raw.IndexOf(';');
            string line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0)
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim().ToUpperInvariant();
                continue;
            }
            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            string key = line[..separator].Trim().ToUpperInvariant();
            result[(section, key)] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, string> ReadPackedEntries(string path, string keySource,
        IReadOnlyCollection<string> requestedEntries)
    {
        var requested = new HashSet<string>(requestedEntries, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] key = Encoding.ASCII.GetBytes(CreateAcdKey(keySource));
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (stream.Length >= 8 && reader.ReadInt32() == -1111)
            _ = reader.ReadInt32();
        else
            stream.Position = 0;

        while (stream.Position < stream.Length && result.Count < requested.Count)
        {
            string name = ReadAcdString(reader);
            int length = ReadBoundedInt32(reader, MaximumEntryDataBytes, "ACD entry data");
            long storedLength = checked((long)length * 4);
            if (storedLength > stream.Length - stream.Position)
                throw new InvalidDataException($"Truncated data.acd entry: {name}");

            if (!requested.Contains(name))
            {
                stream.Position += storedLength;
                continue;
            }

            var data = new byte[length];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = reader.ReadByte();
                stream.Position += 3;
            }
            for (int i = 0; i < data.Length; i++)
                data[i] = unchecked((byte)(data[i] - key[i % key.Length]));
            result[name] = Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
        }
        return result;
    }

    private static string ReadAcdString(BinaryReader reader)
    {
        int length = ReadBoundedInt32(reader, MaximumEntryNameBytes, "ACD entry name");
        if (length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Truncated data.acd entry name");
        string name = Encoding.UTF8.GetString(reader.ReadBytes(length));
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Invalid data.acd entry name");
        return name;
    }

    private static int ReadBoundedInt32(BinaryReader reader, int maximum, string label)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int))
            throw new InvalidDataException($"Truncated {label}");
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Invalid {label} length: {value}");
        return value;
    }

    internal static string CreateAcdKey(string keySource)
    {
        string name = keySource.ToLowerInvariant();
        if (name.Length < 2)
            throw new InvalidDataException("ACD key source is too short");

        int octet1 = name.Sum(character => (int)character);
        int octet2 = 0;
        for (int i = 0; i < name.Length - 1; i += 2)
            octet2 = unchecked(octet2 * name[i] - name[i + 1]);
        int octet3 = 0;
        for (int i = 1; i < name.Length - 3; i += 3)
            octet3 = unchecked(octet3 * name[i] / (name[i + 1] + 27) - 27 - name[i - 1]);
        int octet4 = 5763;
        for (int i = 1; i < name.Length; i++)
            octet4 -= name[i];
        int octet5 = 66;
        for (int i = 1; i < name.Length - 4; i += 4)
            octet5 = unchecked((name[i] + 15) * octet5 * (name[i - 1] + 15) + 22);
        int octet6 = 101;
        for (int i = 0; i < name.Length - 2; i += 2)
            octet6 -= name[i];
        int octet7 = 171;
        for (int i = 0; i < name.Length - 2; i += 2)
            octet7 %= name[i];
        int octet8 = 171;
        for (int i = 0; i < name.Length - 1; i++)
            octet8 = octet8 / name[i] + name[i + 1];

        static byte ToByte(int value) => unchecked((byte)((value % 256 + 256) % 256));
        return string.Join('-', ToByte(octet1), ToByte(octet2), ToByte(octet3), ToByte(octet4),
            ToByte(octet5), ToByte(octet6), ToByte(octet7), ToByte(octet8));
    }
}
