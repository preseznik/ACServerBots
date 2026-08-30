using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ACEditor.App.Controls;
using ACEditor.Core.Editing;
using ACEditor.Core.Formats;
using ACEditor.Core.Infrastructure;
using ACEditor.Core.Models;
using ACEditor.Core.Projects;
using ACEditor.Core.Staging;
using ACEditor.Core.Tools;

namespace ACEditor.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly TrackProjectStore _store = new();
    private readonly UndoRedoStack _undo = new();
    private ToolchainPaths _tools = new();
    private TrackFormatRegistry _formats = null!;
    private TrackProject? _project;
    private TrackNode? _selectedNode;
    private TrackTexture? _selectedTexture;
    private string? _selectedLayout;
    private string _status = "Ready — open an Assetto Corsa or DiRT 2 track folder";
    private bool _isBusy;
    private double _progress;
    private int _sceneRevision;
    private int _textureRevision;

    public MainViewModel()
    {
        RefreshSettings();
    }

    public void RefreshSettings()
    {
        _tools = new ToolchainDiscovery().Discover();
        _formats = new TrackFormatRegistry([
            new AssettoCorsaTrackAdapter(),
            new Dirt2TrackAdapter(_tools)
        ]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public TrackProject? Project
    {
        get => _project;
        private set
        {
            _project = value;
            SelectedTexture = value?.Scene.Textures.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(AvailableLayouts));
            OnPropertyChanged(nameof(CanReplaceSelectedPssgTexture));
        }
    }
    public bool HasProject => Project is not null;
    public IReadOnlyList<string> AvailableLayouts => Project?.LayoutIds ?? [];
    public string? SelectedLayout
    {
        get => _selectedLayout;
        set
        {
            if (string.Equals(_selectedLayout, value, StringComparison.OrdinalIgnoreCase)) return;
            _selectedLayout = value;
            OnPropertyChanged();
        }
    }
    public ObservableCollection<ValidationIssue> Problems { get; } = [];
    public ObservableCollection<string> Jobs { get; } = [];
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }
    public double Progress { get => _progress; private set { _progress = value; OnPropertyChanged(); } }
    public int SceneRevision { get => _sceneRevision; private set { _sceneRevision = value; OnPropertyChanged(); } }
    public int TextureRevision { get => _textureRevision; private set { _textureRevision = value; OnPropertyChanged(); } }
    public TrackTexture? SelectedTexture
    {
        get => _selectedTexture;
        set
        {
            if (ReferenceEquals(_selectedTexture, value)) return;
            _selectedTexture = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExportSelectedTexture));
            OnPropertyChanged(nameof(CanReplaceSelectedPssgTexture));
        }
    }
    public bool CanExportSelectedTexture => SelectedTexture?.EmbeddedData is { Length: > 0 };
    public bool CanReplaceSelectedPssgTexture => Project?.SourceFormat == TrackFormat.Dirt2 &&
        SelectedTexture is not null && TryGetPssgArtifact(SelectedTexture.SourcePath, out _);
    public TrackNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(SelectedName)); OnPropertyChanged(nameof(SelectedSource));
            OnPropertyChanged(nameof(SelectedOwnership)); OnPropertyChanged(nameof(SelectedVisible));
        }
    }
    public string SelectedName
    {
        get => SelectedNode?.Name ?? string.Empty;
        set
        {
            if (SelectedNode is null || value == SelectedNode.Name || string.IsNullOrWhiteSpace(value)) return;
            string before = SelectedNode.Name;
            TrackNode target = SelectedNode;
            _undo.Execute(new PropertyEditCommand<string>("Rename node", next => target.Name = next, before, value));
            RecordProjectEdit("node.name", target, before, value);
            SceneRevision++;
            OnPropertyChanged(); OnPropertyChanged(nameof(Project));
        }
    }
    public string SelectedSource => SelectedNode?.SourceFile ?? "—";
    public string SelectedOwnership => SelectedNode?.Ownership ?? "—";
    public bool SelectedVisible
    {
        get => SelectedNode?.IsVisible ?? false;
        set
        {
            if (SelectedNode is null || value == SelectedNode.IsVisible) return;
            bool before = SelectedNode.IsVisible;
            TrackNode target = SelectedNode;
            _undo.Execute(new PropertyEditCommand<bool>("Change node visibility", next => target.IsVisible = next, before, value));
            RecordProjectEdit("node.visibility", target, before, value);
            SceneRevision++;
            OnPropertyChanged(); OnPropertyChanged(nameof(Project));
        }
    }

    public async Task ImportAsync(string path)
    {
        await RunJobAsync("Import track", async token =>
        {
            var (adapter, probe) = await _formats.ProbeAsync(path, token);
            Status = $"Importing {probe.DisplayName} ({probe.Format})…";
            var progress = new Progress<double>(value => Progress = value * 100);
            Project = await adapter.ImportAsync(path, progress, token);
            SelectedLayout = Project.LayoutIds.FirstOrDefault();
            SelectedNode = Project.Scene.Roots.FirstOrDefault();
            await RefreshValidationAsync(adapter, token);
            Status = $"Loaded {Project.Name}: {Project.Scene.Roots.Count} roots, {Project.Routes.Count} routes";
        });
    }

    public async Task OpenProjectAsync(string path)
    {
        await RunJobAsync("Open project", async token =>
        {
            TrackProject saved = await _store.LoadAsync(path, token);
            ITrackFormatAdapter adapter = _formats.Get(saved.SourceFormat);
            TrackProject hydrated = await adapter.ImportAsync(saved.SourceRoot,
                new Progress<double>(value => Progress = value * 100), token);
            hydrated.ProjectId = saved.ProjectId;
            hydrated.ProjectFile = path;
            hydrated.Name = saved.Name;
            hydrated.ImportedAtUtc = saved.ImportedAtUtc;
            hydrated.ModifiedAtUtc = saved.ModifiedAtUtc;
            hydrated.EditDeltas = saved.EditDeltas;
            hydrated.SourceArtifacts = saved.SourceArtifacts;
            hydrated.LayoutIds = saved.LayoutIds;
            hydrated.Coordinates = saved.Coordinates;
            IReadOnlyList<ValidationIssue> editIssues = ApplySavedEdits(hydrated);
            Project = hydrated;
            SelectedLayout = Project.LayoutIds.FirstOrDefault();
            SelectedNode = Project.Scene.Roots.FirstOrDefault();
            await RefreshValidationAsync(adapter, token);
            foreach (ValidationIssue issue in editIssues) Problems.Add(issue);
            Status = $"Opened {Project.Name}";
        });
    }

    public async Task SaveProjectAsync(string path)
    {
        if (Project is null) return;
        await RunJobAsync("Save project", async token =>
        {
            await _store.SaveAsync(Project, path, token);
            Status = $"Saved {Path.GetFileName(path)}";
        });
    }

    public async Task<StageResult?> StageAsync(string outputDirectory)
    {
        if (Project is null) return null;
        StageResult? result = null;
        await RunJobAsync("Build staged copy", async token =>
        {
            var progress = new Progress<double>(value => Progress = value * 100);
            result = await _formats.Get(Project.SourceFormat).StageAsync(Project,
                new StageOptions(outputDirectory), progress, token);
            Problems.Clear();
            foreach (ValidationIssue issue in result.Issues) Problems.Add(issue);
            Status = result.Succeeded ? $"Staged copy built at {result.OutputDirectory}" : "Staging blocked; review Problems";
        });
        return result;
    }

    public async Task ExportSelectedTextureAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] data = SelectedTexture?.EmbeddedData
            ?? throw new InvalidOperationException("Select a texture with DDS preview data first.");
        await File.WriteAllBytesAsync(path, data, cancellationToken);
        Status = $"Exported {Path.GetFileName(path)}";
    }

    public async Task ReplaceSelectedPssgTextureAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Project is null || SelectedTexture is null ||
            !TryGetPssgArtifact(SelectedTexture.SourcePath, out string relativePssg))
            throw new InvalidOperationException("Select a DiRT 2 PSSG texture first.");
        SourceArtifact artifact = Project.SourceArtifacts.FirstOrDefault(item =>
            item.RelativePath.Replace('\\', '/').Equals(relativePssg, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The source artifact '{relativePssg}' is missing.");
        if (artifact.WriteDisposition != WriteDisposition.RewriteKnown)
            throw new InvalidOperationException($"The PSSG asset '{relativePssg}' is read-only.");

        string fullPath = Path.GetFullPath(path);
        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        DdsImage image = DdsTextureLoader.Parse(bytes);
        string hash = ContentHash.Sha256(fullPath);
        Project.EditDeltas.RemoveAll(edit =>
            edit.Kind.Equals(PssgTextureEditService.EditKind, StringComparison.OrdinalIgnoreCase) &&
            edit.TargetId.Equals(SelectedTexture.SourcePath, StringComparison.OrdinalIgnoreCase));
        Project.EditDeltas.Add(new TrackEditDelta
        {
            Kind = PssgTextureEditService.EditKind,
            TargetId = SelectedTexture.SourcePath,
            RequiredArtifact = relativePssg,
            AfterJson = JsonSerializer.Serialize(new PssgTextureReplacement(fullPath, hash))
        });
        SelectedTexture.EmbeddedData = bytes;
        SelectedTexture.ReplacementPath = fullPath;
        SelectedTexture.ReplacementSha256 = hash;
        SelectedTexture.Width = image.Width;
        SelectedTexture.Height = image.Height;
        SelectedTexture.MipCount = image.Mips.Count;
        SelectedTexture.Format = image.Format.ToString();
        Project.ModifiedAtUtc = DateTimeOffset.UtcNow;
        TextureRevision++;
        OnPropertyChanged(nameof(SelectedTexture));
        OnPropertyChanged(nameof(CanExportSelectedTexture));
        Status = $"Queued DDS replacement for {SelectedTexture.Name}; build a staged copy to write it";
    }

    public void Undo()
    {
        _undo.Undo(); SceneRevision++; OnPropertyChanged(nameof(Project)); OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedVisible));
    }
    public void Redo()
    {
        _undo.Redo(); SceneRevision++; OnPropertyChanged(nameof(Project)); OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedVisible));
    }

    private async Task RefreshValidationAsync(ITrackFormatAdapter adapter, CancellationToken token)
    {
        Problems.Clear();
        foreach (ValidationIssue issue in await adapter.ValidateAsync(Project!, token)) Problems.Add(issue);
    }

    private async Task RunJobAsync(string name, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; Progress = 0; Jobs.Insert(0, $"Running · {name}");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await action(cancellation.Token);
            Jobs[0] = $"Complete · {name}";
        }
        catch (Exception exception)
        {
            Jobs[0] = $"Failed · {name}";
            Problems.Insert(0, new ValidationIssue(ValidationSeverity.Error, "JOB_FAILED", exception.Message));
            Status = $"{name} failed";
        }
        finally { IsBusy = false; }
    }

    private void RecordProjectEdit<T>(string kind, TrackNode target, T before, T after)
    {
        if (Project is null) return;
        Project.EditDeltas.Add(new TrackEditDelta
        {
            Kind = kind, TargetId = target.StableSourceId,
            BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(after)
        });
        Project.ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<ValidationIssue> ApplySavedEdits(TrackProject project)
    {
        var issues = new List<ValidationIssue>();
        var nodes = Flatten(project.Scene.Roots).ToDictionary(node => node.StableSourceId,
            StringComparer.OrdinalIgnoreCase);
        foreach (TrackEditDelta edit in project.EditDeltas)
        {
            if (edit.AfterJson is null) continue;
            if (nodes.TryGetValue(edit.TargetId, out TrackNode? node) && edit.Kind == "node.name")
                node.Name = System.Text.Json.JsonSerializer.Deserialize<string>(edit.AfterJson) ?? node.Name;
            else if (nodes.TryGetValue(edit.TargetId, out node) && edit.Kind == "node.visibility")
                node.IsVisible = System.Text.Json.JsonSerializer.Deserialize<bool>(edit.AfterJson);
            else if (edit.Kind.Equals(PssgTextureEditService.EditKind, StringComparison.OrdinalIgnoreCase))
            {
                TrackTexture? texture = project.Scene.Textures.FirstOrDefault(item =>
                    item.SourcePath.Equals(edit.TargetId, StringComparison.OrdinalIgnoreCase));
                try
                {
                    PssgTextureReplacement replacement = JsonSerializer.Deserialize<PssgTextureReplacement>(edit.AfterJson)
                        ?? throw new InvalidDataException("Replacement metadata is invalid.");
                    if (!File.Exists(replacement.Path)) throw new FileNotFoundException("Replacement DDS is missing.");
                    string hash = ContentHash.Sha256(replacement.Path);
                    if (!hash.Equals(replacement.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Replacement DDS has changed since the project was saved.");
                    byte[] bytes = File.ReadAllBytes(replacement.Path);
                    DdsImage image = DdsTextureLoader.Parse(bytes);
                    if (texture is null) throw new InvalidDataException("The source texture no longer exists.");
                    texture.EmbeddedData = bytes;
                    texture.ReplacementPath = Path.GetFullPath(replacement.Path);
                    texture.ReplacementSha256 = hash;
                    texture.Width = image.Width;
                    texture.Height = image.Height;
                    texture.MipCount = image.Mips.Count;
                    texture.Format = image.Format.ToString();
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                    UnauthorizedAccessException or JsonException)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "PSSG_REPLACEMENT_UNAVAILABLE",
                        $"Texture replacement could not be restored: {exception.Message}", edit.RequiredArtifact,
                        edit.TargetId));
                }
            }
        }
        return issues;
    }

    private static bool TryGetPssgArtifact(string sourcePath, out string relativePssg)
    {
        string normalized = sourcePath.Replace('\\', '/');
        int marker = normalized.IndexOf(".pssg#", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            relativePssg = string.Empty;
            return false;
        }
        relativePssg = normalized[..(marker + ".pssg".Length)];
        return relativePssg.Length > 0 && normalized.Length > marker + ".pssg#".Length;
    }

    private static IEnumerable<TrackNode> Flatten(IEnumerable<TrackNode> roots)
    {
        foreach (TrackNode node in roots)
        {
            yield return node;
            foreach (TrackNode child in Flatten(node.Children)) yield return child;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
