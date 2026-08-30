using System.Globalization;
using System.Reflection;
using System.Xml;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Staging;
using ACEditor.Core.Tools;
using ACEditor.Core.Validation;

namespace ACEditor.Core.Formats;

public sealed class Dirt2TrackAdapter : ITrackFormatAdapter
{
    private static readonly HashSet<string> OpaqueExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cqtc", ".clm", ".grs", ".cns", ".vis", ".gssp", ".htf", ".bin" };
    private readonly SafeStagingService _staging = new();
    private readonly ToolchainPaths _tools;
    private readonly EgoBinaryXmlBridge _xml;

    public Dirt2TrackAdapter(ToolchainPaths tools)
    {
        _tools = tools;
        _xml = new EgoBinaryXmlBridge(tools.EgoPssgEditorRoot);
    }

    public TrackFormat Format => TrackFormat.Dirt2;

    public Task<TrackProbeResult> ProbeAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(sourcePath)) return new TrackProbeResult(Format, 0, "DiRT 2", [], []);
            var evidence = new List<string>();
            if (File.Exists(Path.Combine(sourcePath, "tracksplit.pssg"))) evidence.Add("tracksplit.pssg");
            if (Directory.EnumerateFiles(sourcePath, "*.pssg", SearchOption.AllDirectories).Any()) evidence.Add("PSSG assets");
            if (Directory.EnumerateFiles(sourcePath, "*.jpk", SearchOption.AllDirectories).Any()) evidence.Add("JPK collision archive");
            if (Directory.EnumerateFiles(sourcePath, "ai_track.xml", SearchOption.AllDirectories).Any()) evidence.Add("binary XML route data");
            var issues = new List<ValidationIssue>();
            if (!_xml.IsAvailable)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "EGO_TOOL_MISSING",
                    "EgoEngineLibrary 15.0.0 is not configured; binary XML route preview is unavailable."));
            return new TrackProbeResult(Format, Math.Min(100, evidence.Count * 25),
                Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath)), evidence, issues);
        }, cancellationToken);

    public Task<TrackProject> ImportAsync(string sourcePath, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run(() => Import(sourcePath, progress, cancellationToken), cancellationToken);

    private TrackProject Import(string sourcePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string root = PathRules.NormalizeDirectory(sourcePath);
        var project = new TrackProject
        {
            Name = Path.GetFileName(root), SourceRoot = root, SourceFormat = TrackFormat.Dirt2,
            Coordinates = new CoordinateContract
            {
                Source = "EGO track scene right-handed, Y-up, metres",
                Conversion = "identity into canonical scene; raw matrices and source paths retained",
                ConversionAppliedExactlyOnce = true
            }
        };
        if (_tools.EgoPssgEditorRoot is not null)
        {
            string ego = Path.Combine(_tools.EgoPssgEditorRoot, "EgoEngineLibrary.dll");
            if (File.Exists(ego))
                project.ToolchainVersions["EgoEngineLibrary"] =
                    AssemblyName.GetAssemblyName(ego).Version?.ToString() ?? "unknown";
            string editor = Path.Combine(_tools.EgoPssgEditorRoot, "Ego PSSG Editor.exe");
            if (File.Exists(editor))
                project.ToolchainVersions["Ego PSSG Editor"] =
                    System.Diagnostics.FileVersionInfo.GetVersionInfo(editor).FileVersion ?? "unknown";
        }

        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[i];
            string relative = Path.GetRelativePath(root, file);
            WriteDisposition disposition = Classify(file);
            var artifact = new SourceArtifact
            {
                RelativePath = relative, Sha256 = ContentHash.Sha256(file), Length = new FileInfo(file).Length,
                WriteDisposition = disposition,
                BlockReason = disposition == WriteDisposition.Blocked
                    ? "This DiRT 2 record is byte-preserved; no verified encoder is available for edits."
                    : null
            };
            project.SourceArtifacts.Add(artifact);
            if (Path.GetExtension(file).Equals(".pssg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    project.Scene.Roots.Add(EgoPssgTrackReader.Read(file, project.Scene, root,
                        ResolveOwnership(relative)));
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    artifact.WriteDisposition = WriteDisposition.Blocked;
                    artifact.BlockReason = $"PSSG import failed and will be byte-preserved: {exception.Message}";
                    project.Scene.Roots.Add(new TrackNode
                    {
                        Name = Path.GetFileName(file), StableSourceId = relative.Replace('\\', '/'),
                        SourceFile = relative, Ownership = ResolveOwnership(relative), IsLocked = true,
                        IsVisible = true
                    });
                }
            }
            progress?.Report((i + 1) * 0.7 / Math.Max(1, files.Length));
        }

        string[] aiFiles = Directory.EnumerateFiles(root, "ai_track.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < aiFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string layoutId = new DirectoryInfo(Path.GetDirectoryName(aiFiles[i])!).Name;
            if (!project.LayoutIds.Contains(layoutId, StringComparer.OrdinalIgnoreCase)) project.LayoutIds.Add(layoutId);
            if (_xml.IsAvailable) project.Routes.Add(ReadRoute(root, aiFiles[i]));
            progress?.Report(0.7 + (i + 1) * 0.3 / Math.Max(1, aiFiles.Length));
        }
        progress?.Report(1);
        return project;
    }

    private TrackRoute ReadRoute(string root, string path)
    {
        XmlDocument document = _xml.Open(path);
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var route = new TrackRoute
        {
            Id = relative, DisplayName = new DirectoryInfo(Path.GetDirectoryName(path)!).Name
        };
        XmlNodeList gates = document.SelectNodes("/ai_track_data/track/gates/gate")
                            ?? throw new InvalidDataException($"DiRT 2 route has no gates: {relative}");
        foreach (XmlElement gate in gates)
        {
            var position = ReadVector(gate.SelectSingleNode("position"));
            var normal = ReadVector(gate.SelectSingleNode("normal"));
            XmlElement? racingLine = gate.SelectNodes("waypoints/waypoint")?.OfType<XmlElement>()
                .FirstOrDefault(element => element.GetAttribute("type").Equals("racing_line", StringComparison.OrdinalIgnoreCase));
            float offset = racingLine is null ? 0 : ReadFloat(racingLine, "length");
            route.Points.Add(new RoutePoint
            {
                Position = new Position3(position.X + normal.X * offset,
                    position.Y + normal.Y * offset, position.Z + normal.Z * offset),
                LeftWidth = 4, RightWidth = 4
            });
        }
        route.IsClosed = route.Points.Count > 2 &&
                         System.Numerics.Vector3.Distance(route.Points[0].Position.ToVector(),
                             route.Points[^1].Position.ToVector()) < 50;
        return route;
    }

    private static Position3 ReadVector(XmlNode? node)
    {
        if (node is not XmlElement element) throw new InvalidDataException("Route vector is missing.");
        if (element.HasAttribute("x") || element.SelectSingleNode("x") is not null)
            return new Position3(ReadFloat(element, "x"), ReadFloat(element, "y"), ReadFloat(element, "z"));
        string[] values = element.InnerText.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 3 ||
            !float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            throw new InvalidDataException($"Invalid route vector '{element.InnerText}'.");
        return new Position3(x, y, z);
    }

    private static float ReadFloat(XmlElement element, string name)
    {
        string value = element.GetAttribute(name);
        if (string.IsNullOrWhiteSpace(value)) value = element.SelectSingleNode(name)?.InnerText ?? string.Empty;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            throw new InvalidDataException($"Invalid {name} value '{value}'.");
        return result;
    }

    private static string ResolveOwnership(string relative)
    {
        string first = relative.Replace('\\', '/').Split('/')[0];
        return first.StartsWith("route_", StringComparison.OrdinalIgnoreCase) ? first : "shared";
    }

    private WriteDisposition Classify(string path)
    {
        string extension = Path.GetExtension(path);
        if (OpaqueExtensions.Contains(extension)) return WriteDisposition.Blocked;
        if (extension.Equals(".pssg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpk", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return _xml.IsAvailable ? WriteDisposition.RewriteKnown : WriteDisposition.Blocked;
        return WriteDisposition.CopyUnchanged;
    }

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(TrackProject project,
        CancellationToken cancellationToken = default)
    {
        var issues = TrackValidator.Validate(project).ToList();
        if (!_xml.IsAvailable)
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "EGO_TOOL_REQUIRED",
                "Configure Ego PSSG Editor 12.1.1 / EgoEngineLibrary 15.0.0 before native DiRT 2 staging."));
        if (!Flatten(project.Scene.Roots).Any(node => node.Mesh is not null))
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "PSSG_GEOMETRY_LOCKED",
                "No supported PSSG render geometry was found; those assets remain byte-preserved."));
        int lockedPssgAssets = project.Scene.Roots.Count(node => node.IsLocked &&
            Path.GetExtension(node.SourceFile).Equals(".pssg", StringComparison.OrdinalIgnoreCase));
        if (lockedPssgAssets > 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "PSSG_PARTIAL_IMPORT",
                $"{lockedPssgAssets} PSSG assets contain no supported render or texture records and remain locked."));
        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    public async Task<StageResult> StageAsync(TrackProject project, StageOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ValidationIssue> validation = await ValidateAsync(project, cancellationToken);
        if (validation.Any(issue => issue.Severity == ValidationSeverity.Error))
            return new StageResult { OutputDirectory = options.OutputDirectory, Issues = validation.ToList() };
        return await _staging.StageAsync(project, options, progress, cancellationToken,
            (stagedRoot, token) => PssgTextureEditService.Apply(project, stagedRoot, token));
    }

    private static IEnumerable<TrackNode> Flatten(IEnumerable<TrackNode> roots)
    {
        foreach (TrackNode node in roots)
        {
            yield return node;
            foreach (TrackNode child in Flatten(node.Children)) yield return child;
        }
    }
}
