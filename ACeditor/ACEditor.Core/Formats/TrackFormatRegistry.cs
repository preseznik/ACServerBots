using ACEditor.Core.Models;

namespace ACEditor.Core.Formats;

public sealed class TrackFormatRegistry(IEnumerable<ITrackFormatAdapter> adapters)
{
    private readonly IReadOnlyList<ITrackFormatAdapter> _adapters = adapters.ToArray();

    public async Task<(ITrackFormatAdapter Adapter, TrackProbeResult Probe)> ProbeAsync(string path,
        CancellationToken cancellationToken = default)
    {
        TrackProbeResult[] probes = await Task.WhenAll(_adapters.Select(adapter =>
            adapter.ProbeAsync(path, cancellationToken)));
        TrackProbeResult best = probes.OrderByDescending(probe => probe.Confidence).First();
        if (best.Confidence <= 0)
            throw new InvalidDataException("The folder is not a recognized Assetto Corsa or DiRT 2 track.");
        return (_adapters.Single(adapter => adapter.Format == best.Format), best);
    }

    public ITrackFormatAdapter Get(TrackFormat format) =>
        _adapters.Single(adapter => adapter.Format == format);
}
