using System.Numerics;
using System.Text.Json.Serialization;

namespace ACEditor.Core.Models;

public enum TrackFormat { Unknown, AssettoCorsa, Dirt2 }
public enum WriteDisposition { CopyUnchanged, RewriteKnown, Blocked }
public enum ValidationSeverity { Information, Warning, Error }
public enum CollisionRole { Unspecified, Driveable, Terrain, Barrier, Camera, Water, VisualOnly }
public enum MaterialBlendMode : byte { Opaque = 0, AlphaBlend = 1, AlphaToCoverage = 2 }
public enum MaterialDepthMode { Normal = 0, NoWrite = 1, Off = 2 }

public readonly record struct Position3(float X, float Y, float Z)
{
    public static Position3 FromVector(Vector3 value) => new(value.X, value.Y, value.Z);
    public Vector3 ToVector() => new(X, Y, Z);
}

public readonly record struct Transform44(
    float M11, float M12, float M13, float M14,
    float M21, float M22, float M23, float M24,
    float M31, float M32, float M33, float M34,
    float M41, float M42, float M43, float M44)
{
    public static Transform44 Identity { get; } = FromMatrix(Matrix4x4.Identity);
    public static Transform44 FromMatrix(Matrix4x4 value) => new(
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44);
    public Matrix4x4 ToMatrix() => new(
        M11, M12, M13, M14,
        M21, M22, M23, M24,
        M31, M32, M33, M34,
        M41, M42, M43, M44);
}

public sealed class CoordinateContract
{
    public string Canonical { get; set; } = "right-handed, Y-up, metres";
    public string Source { get; set; } = "unknown";
    public string Conversion { get; set; } = "not yet established";
    public bool ConversionAppliedExactlyOnce { get; set; }
}

public sealed class SourceArtifact
{
    public required string RelativePath { get; set; }
    public required string Sha256 { get; set; }
    public long Length { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WriteDisposition WriteDisposition { get; set; }
    public string? BlockReason { get; set; }
}

public sealed class TrackProject
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled track";
    public string ProjectFile { get; set; } = string.Empty;
    public string SourceRoot { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TrackFormat SourceFormat { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public CoordinateContract Coordinates { get; set; } = new();
    public Dictionary<string, string> ToolchainVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> LayoutIds { get; set; } = [];
    public List<SourceArtifact> SourceArtifacts { get; set; } = [];
    [JsonIgnore]
    public TrackScene Scene { get; set; } = new();
    [JsonIgnore]
    public List<TrackRoute> Routes { get; set; } = [];
    public List<TrackEditDelta> EditDeltas { get; set; } = [];
}

public sealed class TrackScene
{
    public List<TrackNode> Roots { get; set; } = [];
    public List<TrackMaterial> Materials { get; set; } = [];
    public List<TrackTexture> Textures { get; set; } = [];
    public List<CollisionLayer> CollisionLayers { get; set; } = [];
}

public sealed class TrackNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StableSourceId { get; set; } = string.Empty;
    public string Name { get; set; } = "Node";
    public string SourceFile { get; set; } = string.Empty;
    public string Ownership { get; set; } = "shared";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public int Lod { get; set; }
    public Transform44 Transform { get; set; } = Transform44.Identity;
    public TrackMesh? Mesh { get; set; }
    public List<TrackNode> Children { get; set; } = [];
}

public sealed class TrackMesh
{
    public string Name { get; set; } = string.Empty;
    public int MaterialIndex { get; set; } = -1;
    public bool SourceCastsShadows { get; set; }
    public bool SourceVisible { get; set; } = true;
    public bool SourceTransparent { get; set; }
    public bool SourceRenderable { get; set; } = true;
    public List<Position3> Positions { get; set; } = [];
    public List<Position3> Normals { get; set; } = [];
    public List<Position3> TextureCoordinates { get; set; } = [];
    public List<int> Indices { get; set; } = [];
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionRole CollisionRole { get; set; }
}

public sealed class TrackMaterial
{
    public string Name { get; set; } = string.Empty;
    public string SourceShader { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MaterialBlendMode BlendMode { get; set; }
    public bool AlphaTested { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MaterialDepthMode DepthMode { get; set; }
    public bool IsApproximation { get; set; }
    public Dictionary<string, float[]> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TextureSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrackTexture
{
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipCount { get; set; }
    [JsonIgnore]
    public byte[]? EmbeddedData { get; set; }
    [JsonIgnore]
    public string? ReplacementPath { get; set; }
    [JsonIgnore]
    public string? ReplacementSha256 { get; set; }
    [JsonIgnore]
    public bool HasReplacement => !string.IsNullOrWhiteSpace(ReplacementPath);
}

public sealed record PssgTextureReplacement(string Path, string Sha256);

public sealed class TrackRoute
{
    public string Id { get; set; } = "route";
    public string DisplayName { get; set; } = "Route";
    public bool IsClosed { get; set; }
    public List<RoutePoint> Points { get; set; } = [];
    public List<RouteMarker> Markers { get; set; } = [];
}

public sealed class RoutePoint
{
    public Position3 Position { get; set; }
    public float LeftWidth { get; set; }
    public float RightWidth { get; set; }
    public float Speed { get; set; }
}

public sealed class RouteMarker
{
    public string Kind { get; set; } = "checkpoint";
    public string Name { get; set; } = string.Empty;
    public Position3 Position { get; set; }
}

public sealed class CollisionLayer
{
    public string Name { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionRole Role { get; set; }
    public List<Guid> NodeIds { get; set; } = [];
    public bool IsEditable { get; set; }
}

public sealed class TrackEditDelta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? RequiredArtifact { get; set; }
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message,
    string? SourcePath = null, string? TargetId = null);

public sealed record TrackProbeResult(TrackFormat Format, int Confidence, string DisplayName,
    IReadOnlyList<string> Evidence, IReadOnlyList<ValidationIssue> Issues);

public sealed record StageOptions(string OutputDirectory, bool OverwriteExisting = false);

public sealed class StageResult
{
    public string OutputDirectory { get; init; } = string.Empty;
    public List<SourceArtifact> Manifest { get; init; } = [];
    public List<ValidationIssue> Issues { get; init; } = [];
    public bool Succeeded => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}
