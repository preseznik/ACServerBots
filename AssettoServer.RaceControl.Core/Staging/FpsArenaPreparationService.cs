using System.Diagnostics;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Storage;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed class FpsArenaPreparationService
{
    private readonly FpsArenaStore _store;
    private readonly RaceControlPaths _paths;

    public FpsArenaPreparationService(FpsArenaStore store, RaceControlPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public async Task<FpsArenaDefinition> PrepareAsync(RaceControlPreset preset, AcTrackLayout track,
        IProgress<StagingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!track.TrackId.Equals(preset.TrackId, StringComparison.OrdinalIgnoreCase)
            || !track.LayoutId.Equals(preset.TrackLayoutId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The FPS preparation track does not match the selected preset.",
                nameof(track));

        string executable = Path.Combine(preset.ServerPayloadPath, "AssettoServer.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Published AssettoServer.exe was not found.", executable);

        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"asrc-fps-arena-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        string metadataOutput = Path.Combine(temporaryRoot, "fps-arena.json");
        string geometryOutput = Path.Combine(temporaryRoot, "fps-arena-geometry.bin");
        string navigationOutput = Path.Combine(temporaryRoot, "fps-arena-navigation.bin");
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
            startInfo.ArgumentList.Add(metadataOutput);
            startInfo.ArgumentList.Add("--fps-geometry-output");
            startInfo.ArgumentList.Add(geometryOutput);
            startInfo.ArgumentList.Add("--fps-navigation-output");
            startInfo.ArgumentList.Add(navigationOutput);
            startInfo.ArgumentList.Add("--fps-bounds-padding");
            startInfo.ArgumentList.Add(preset.Fps.ArenaBoundsPaddingMeters.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AddCollisionOverrides(startInfo, preset.Fps.Arena);

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, args) => Report(args.Data);
            process.ErrorDataReceived += (_, args) => Report(args.Data);
            if (!process.Start()) throw new InvalidOperationException("Could not start FPS arena preparation.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(metadataOutput)
                || !File.Exists(geometryOutput) || !File.Exists(navigationOutput))
                throw new InvalidOperationException($"FPS arena preparation failed with exit code {process.ExitCode}.");

            var arena = System.Text.Json.JsonSerializer.Deserialize<FpsArenaDefinition>(
                await File.ReadAllTextAsync(metadataOutput, cancellationToken),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("FPS arena preparation returned an empty document.");
            PersistPreparedArena(preset, track, arena, geometryOutput, navigationOutput);
            progress?.Report(new("FPS arena",
                "Cached prepared FPS geometry and navigation for server staging.", 0.95));
            return arena;

            void Report(string? message)
            {
                if (!string.IsNullOrWhiteSpace(message))
                    progress?.Report(new("FPS arena", message, 0.5));
            }
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    internal void PersistPreparedArena(RaceControlPreset preset, AcTrackLayout track,
        FpsArenaDefinition arena, string geometryPath, string navigationPath)
    {
        var cachePaths = PreparedPhysicsAssetCache.GetFpsPaths(_paths, preset, track);
        cachePaths.StoreFrom(geometryPath, navigationPath);
        _store.Save(arena);
    }

    internal static void AddCollisionOverrides(ProcessStartInfo startInfo,
        FpsArenaDefinition? arena)
    {
        if (arena is null) return;
        if (arena.CollisionIncludeMeshes.Count > 0)
        {
            startInfo.ArgumentList.Add("--fps-collision-include");
            startInfo.ArgumentList.Add(string.Join(';', arena.CollisionIncludeMeshes));
        }
        if (arena.CollisionExcludeMeshes.Count > 0)
        {
            startInfo.ArgumentList.Add("--fps-collision-exclude");
            startInfo.ArgumentList.Add(string.Join(';', arena.CollisionExcludeMeshes));
        }
    }
}
