using ACEditor.Core.Models;

namespace ACEditor.Core.Formats;

public interface ITrackFormatAdapter
{
    TrackFormat Format { get; }
    Task<TrackProbeResult> ProbeAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<TrackProject> ImportAsync(string sourcePath, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidationIssue>> ValidateAsync(TrackProject project,
        CancellationToken cancellationToken = default);
    Task<StageResult> StageAsync(TrackProject project, StageOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
