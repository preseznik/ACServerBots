using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.Core.Staging;

public sealed record StagingProgress(string Stage, string Message, double? Fraction = null);

public sealed record StagedInstance(
    string RootPath,
    string ExecutablePath,
    string PresetName,
    string ShutdownFilePath,
    int SlotCount,
    int BotSlotCount,
    bool PhysicsCacheHit);

public sealed class ServerInstanceStager
{
    public const string PresetName = "race-control";

    private readonly RaceControlPaths _paths;
    private readonly RaceControlValidator _validator;
    private readonly ServerConfigurationRenderer _renderer;

    public ServerInstanceStager(RaceControlPaths paths, RaceControlValidator validator, ServerConfigurationRenderer renderer)
    {
        _paths = paths;
        _validator = validator;
        _renderer = renderer;
    }

    public async Task<StagedInstance> StageAsync(
        RaceControlPreset preset,
        AcContentCatalog catalog,
        IProgress<StagingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(preset, catalog);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                validation.Messages.Where(message => message.Severity == ValidationSeverity.Error).Select(message => message.Message)));
        }

        _paths.EnsureCreated();
        var root = GetUniqueInstanceDirectory(preset);
        Directory.CreateDirectory(root);
        progress?.Report(new("Copy", "Copying the standalone server payload…", 0));
        await CopyDirectoryAsync(preset.ServerPayloadPath, root, progress, cancellationToken);
        Directory.CreateDirectory(Path.Combine(root, "plugins"));

        var rendered = _renderer.Render(preset, catalog);
        var presetRoot = Path.Combine(root, "presets", PresetName);
        Directory.CreateDirectory(presetRoot);
        rendered.ServerConfiguration.Save(Path.Combine(presetRoot, "server_cfg.ini"));
        rendered.EntryList.Save(Path.Combine(presetRoot, "entry_list.ini"));
        await File.WriteAllTextAsync(Path.Combine(presetRoot, "extra_cfg.yml"), rendered.ExtraConfiguration, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(presetRoot, "welcome.txt"), "AssettoServer Race Control LAN event", cancellationToken);
        Directory.CreateDirectory(Path.Combine(root, "cfg"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "cfg", "data_track_params.ini"),
            "; LAN Race Control uses the configured UTC fallback." + Environment.NewLine,
            cancellationToken);

        progress?.Report(new("Content", "Copying checksums and AI line…", 0.7));
        CopyServerContent(root, rendered);

        var cacheHit = false;
        if (preset.Bots.Enabled)
        {
            var physicsOutput = Path.Combine(presetRoot, "race-physics.bin");
            var cachePath = GetPhysicsCachePath(preset, rendered);
            if (File.Exists(cachePath))
            {
                File.Copy(cachePath, physicsOutput, true);
                cacheHit = true;
                progress?.Report(new("Physics", "Reused prepared rigid-body geometry from the local cache.", 0.9));
            }
            else
            {
                progress?.Report(new("Physics", "Preparing exact track, grid, and car collision geometry…", 0.8));
                await PreparePhysicsAsync(root, preset, rendered, physicsOutput, progress, cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.Copy(physicsOutput, cachePath, true);
            }
        }

        var shutdownPath = Path.Combine(root, "shutdown.signal");
        var botSlots = preset.Bots.Enabled
            ? rendered.EffectiveGrid.Count(slot => slot.Mode != SlotMode.None)
            : 0;
        var manifest = new
        {
            schemaVersion = 1,
            presetId = preset.Id,
            presetName = preset.Name,
            createdAt = DateTimeOffset.Now,
            track = rendered.Track.Key,
            cars = rendered.Cars.Select(car => car.Id).ToArray(),
            slots = rendered.EffectiveGrid.Count,
            botSlots,
            physicsFidelity = preset.Bots.PhysicsFidelity.ToString(),
            physicsCacheHit = cacheHit,
            bindAddress = preset.Network.BindAddress,
            ports = new { preset.Network.TcpPort, preset.Network.UdpPort, preset.Network.HttpPort },
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, "race-control-instance.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        progress?.Report(new("Ready", $"Staged {rendered.EffectiveGrid.Count} slots at {root}", 1));
        return new StagedInstance(
            root,
            Path.Combine(root, "AssettoServer.exe"),
            PresetName,
            shutdownPath,
            rendered.EffectiveGrid.Count,
            botSlots,
            cacheHit);
    }

    private string GetUniqueInstanceDirectory(RaceControlPreset preset)
    {
        var candidate = _paths.GetInstanceDirectory(preset.Name, preset.Id);
        for (var suffix = 2; Directory.Exists(candidate); suffix++)
        {
            candidate = _paths.GetInstanceDirectory(preset.Name, preset.Id) + $"-{suffix}";
        }

        return candidate;
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        IProgress<StagingProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = files[index];
            var relative = Path.GetRelativePath(source, sourcePath);
            var destinationPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await input.CopyToAsync(output, cancellationToken);
            if (index % 20 == 0 || index + 1 == files.Length)
            {
                progress?.Report(new("Copy", $"Copied {index + 1} of {files.Length} server files…", files.Length == 0 ? 0.6 : 0.6 * (index + 1) / files.Length));
            }
        }
    }

    private static void CopyServerContent(string root, RenderedServerConfiguration rendered)
    {
        foreach (var car in rendered.Cars)
        {
            if (car.DataAcdPath is null)
            {
                throw new InvalidOperationException($"Car has no data.acd: {car.Id}");
            }

            var carDestination = Path.Combine(root, "content", "cars", car.Id);
            Directory.CreateDirectory(carDestination);
            File.Copy(car.DataAcdPath, Path.Combine(carDestination, "data.acd"), true);
        }

        if (!File.Exists(rendered.Track.FastLanePath))
            return;

        var aiDestination = string.IsNullOrEmpty(rendered.Track.LayoutId)
            ? Path.Combine(root, "content", "tracks", rendered.Track.TrackId, "ai")
            : Path.Combine(root, "content", "tracks", rendered.Track.TrackId, rendered.Track.LayoutId, "ai");
        Directory.CreateDirectory(aiDestination);
        File.Copy(rendered.Track.FastLanePath, Path.Combine(aiDestination, "fast_lane.ai"), true);
    }

    private string GetPhysicsCachePath(RaceControlPreset preset, RenderedServerConfiguration rendered)
    {
        var inputs = new List<string>
        {
            Path.Combine(preset.ServerPayloadPath, "AssettoServer.exe"),
            rendered.Track.ModelsIniPath,
            rendered.Track.FastLanePath,
        };
        using var models = File.Exists(rendered.Track.ModelsIniPath) ? File.OpenText(rendered.Track.ModelsIniPath) : null;
        if (models is not null)
        {
            string? line;
            while ((line = models.ReadLine()) is not null)
            {
                var separator = line.IndexOf('=');
                if (separator > 0 && line[..separator].Trim().Equals("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(Path.Combine(rendered.Track.RootPath, line[(separator + 1)..].Trim()));
                }
            }
        }

        inputs.AddRange(rendered.Cars.Select(car => car.ColliderPath ?? string.Empty));
        var keyBuilder = new StringBuilder();
        foreach (var path in inputs.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            keyBuilder.Append(Path.GetFullPath(path)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).AppendLine();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyBuilder.ToString()))).ToLowerInvariant();
        return Path.Combine(_paths.CacheDirectory, "Physics", $"race-physics-{hash}.bin");
    }

    private static async Task PreparePhysicsAsync(
        string root,
        RaceControlPreset preset,
        RenderedServerConfiguration rendered,
        string output,
        IProgress<StagingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(root, "AssettoServer.exe"),
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--prepare-race-physics");
        startInfo.ArgumentList.Add("--ac-root");
        startInfo.ArgumentList.Add(preset.AssettoCorsaRoot);
        startInfo.ArgumentList.Add("--track");
        startInfo.ArgumentList.Add(rendered.Track.TrackId);
        if (!string.IsNullOrEmpty(rendered.Track.LayoutId))
        {
            startInfo.ArgumentList.Add("--track-config");
            startInfo.ArgumentList.Add(rendered.Track.LayoutId);
        }
        startInfo.ArgumentList.Add("--cars");
        startInfo.ArgumentList.Add(string.Join(';', rendered.Cars.Select(car => car.Id)));
        startInfo.ArgumentList.Add("--physics-output");
        startInfo.ArgumentList.Add(output);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                progress?.Report(new("Physics", args.Data, 0.85));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                progress?.Report(new("Physics", args.Data, 0.85));
            }
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start AssettoServer physics preparation.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            throw new InvalidOperationException($"Rigid-body physics preparation failed with exit code {process.ExitCode}.");
        }
    }
}
