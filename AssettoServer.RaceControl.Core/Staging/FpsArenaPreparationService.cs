using System.Diagnostics;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Storage;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed class FpsArenaPreparationService
{
    private readonly FpsArenaStore _store;

    public FpsArenaPreparationService(FpsArenaStore store) => _store = store;

    public async Task<FpsArenaDefinition> PrepareAsync(RaceControlPreset preset,
        IProgress<StagingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string executable = Path.Combine(preset.ServerPayloadPath, "AssettoServer.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Published AssettoServer.exe was not found.", executable);

        string temporary = Path.Combine(Path.GetTempPath(), $"asrc-fps-arena-{Guid.NewGuid():N}.json");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = preset.ServerPayloadPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--prepare-fps-arena");
            startInfo.ArgumentList.Add("--ac-root");
            startInfo.ArgumentList.Add(preset.AssettoCorsaRoot);
            startInfo.ArgumentList.Add("--track");
            startInfo.ArgumentList.Add(preset.TrackId);
            if (!string.IsNullOrWhiteSpace(preset.TrackLayoutId))
            {
                startInfo.ArgumentList.Add("--track-config");
                startInfo.ArgumentList.Add(preset.TrackLayoutId);
            }
            startInfo.ArgumentList.Add("--fps-arena-output");
            startInfo.ArgumentList.Add(temporary);

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, args) => Report(args.Data);
            process.ErrorDataReceived += (_, args) => Report(args.Data);
            if (!process.Start()) throw new InvalidOperationException("Could not start FPS arena preparation.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(temporary))
                throw new InvalidOperationException($"FPS arena preparation failed with exit code {process.ExitCode}.");

            // Deserialize directly: the temporary filename is intentionally unique and not a sidecar key.
            var arena = System.Text.Json.JsonSerializer.Deserialize<FpsArenaDefinition>(
                await File.ReadAllTextAsync(temporary, cancellationToken),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("FPS arena preparation returned an empty document.");
            _store.Save(arena);
            return arena;

            void Report(string? message)
            {
                if (!string.IsNullOrWhiteSpace(message))
                    progress?.Report(new("FPS arena", message, 0.5));
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
