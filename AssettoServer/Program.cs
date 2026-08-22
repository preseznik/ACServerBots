using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Ai.Physics;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Runtime;
using AssettoServer.Utils;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using CommandLine;
using DotNext.Collections.Generic;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using Parser = CommandLine.Parser;

namespace AssettoServer;

public static class Program
{
#if DEBUG
    public static readonly bool IsDebugBuild = true;
#else
    public static readonly bool IsDebugBuild = false;
#endif
    
    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
    private class Options
    {
        [Option('p', "preset", Required = false, SetName = "AssettoServer", HelpText = "Configuration preset")]
        public string Preset { get; set; } = "";

        [Option('c', Required = false, SetName = "Content Manager compatibility", HelpText = "Path to server configuration")]
        public string ServerCfgPath { get; set; } = "";

        [Option('e', Required = false, SetName = "Content Manager compatibility", HelpText = "Path to entry list")]
        public string EntryListPath { get; set; } = "";

        [Option("plugins-from-workdir", Required = false, HelpText = "Additionally load plugins from working directory")]
        public bool LoadPluginsFromWorkdir { get; set; } = false;

        [Option("verbose", Required = false, HelpText = "Change log level to verbose")]
        public bool UseVerboseLogging { get; set; } = false;
        
        [Option('r',"use-random-preset", Required = false, HelpText = "Use a random available configuration preset")]
        public bool UseRandomPreset { get; set; } = false;
        
        [Option('g',"generate-config", Required = false, HelpText = "Generate configuration file for all installed plugins")]
        public bool GenerateConfigs { get; set; } = false;

        [Option("prepare-race-physics", Required = false, HelpText = "Build race-physics.bin from an Assetto Corsa installation and exit")]
        public bool PrepareRacePhysics { get; set; }

        [Option("ac-root", Required = false, HelpText = "Assetto Corsa installation root used by --prepare-race-physics")]
        public string AssettoCorsaRoot { get; set; } = "";

        [Option("track", Required = false, HelpText = "Track id used by --prepare-race-physics")]
        public string PhysicsTrack { get; set; } = "";

        [Option("track-config", Required = false, HelpText = "Track layout id used by --prepare-race-physics")]
        public string PhysicsTrackConfig { get; set; } = "";

        [Option("cars", Required = false, HelpText = "Semicolon-separated car ids used by --prepare-race-physics")]
        public string PhysicsCars { get; set; } = "";

        [Option("physics-output", Required = false, HelpText = "Output race-physics.bin path used by --prepare-race-physics")]
        public string PhysicsOutput { get; set; } = "";

        [Option("shutdown-file", Required = false, SetName = "AssettoServer", HelpText = "Gracefully stop when this file is created")]
        public string ShutdownFile { get; set; } = "";

        [Option("simulate-race", Required = false, SetName = "AssettoServer", HelpText = "Run a bot-only race using deterministic virtual time and no network listeners")]
        public bool SimulateRace { get; set; }

        [Option("simulation-output", Required = false, HelpText = "Directory for race simulation JSONL telemetry and summary")]
        public string SimulationOutput { get; set; } = "simulation";

        [Option("simulation-seed", Required = false, HelpText = "Deterministic race bot random seed")]
        public int SimulationSeed { get; set; } = 1;

        [Option("simulation-max-minutes", Required = false, HelpText = "Maximum simulated time before stopping")]
        public int SimulationMaximumMinutes { get; set; } = 30;

        [Option("simulation-max-wall-seconds", Required = false, HelpText = "Maximum wall-clock runtime before stopping")]
        public int SimulationMaximumWallSeconds { get; set; } = 300;

        [Option("simulation-sample-ms", Required = false, HelpText = "Structured telemetry sample interval in simulated milliseconds")]
        public int SimulationSampleMilliseconds { get; set; } = 500;

        [Option("race-control-directory", Required = false, HelpText = "Local Race Control snapshot and command directory")]
        public string? RaceControlDirectory { get; set; }
    }

    private class StartOptions
    {
        public string? Preset { get; init; }
        public string? ServerCfgPath { get; init; }
        public string? EntryListPath { get; init; }
        public PortOverrides? PortOverrides { get; init; }
    }

    public static bool IsContentManager { get; private set; }
    public static ConfigurationLocations? ConfigurationLocations { get; private set; }
    
    private static bool _loadPluginsFromWorkdir;
    private static bool _generatePluginConfigs;
    private static TaskCompletionSource<StartOptions> _restartTask = new();
    
    internal static async Task Main(string[] args)
    {
        SetupFluentValidation();
        SetupMetrics();
        DetectContentManager();
        
        var options = Parser.Default.ParseArguments<Options>(args).Value;
        if (options == null) return;

        if (!string.IsNullOrWhiteSpace(options.ShutdownFile) && File.Exists(options.ShutdownFile))
        {
            File.Delete(options.ShutdownFile);
        }

        if (options.PrepareRacePhysics)
        {
            PrepareRacePhysics(options);
            return;
        }

        _loadPluginsFromWorkdir = options.LoadPluginsFromWorkdir;
        _generatePluginConfigs = options.GenerateConfigs;
        
        if (IsContentManager)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        
        if (options.UseRandomPreset)
        {
            var presetsPath = Path.Join(AppContext.BaseDirectory, "presets");
            var presets = Path.Exists(presetsPath) ? 
                Directory.EnumerateDirectories("presets").Select(Path.GetFileName).OfType<string>().ToArray() : [];
            
            if (presets.Length > 0)
                options.Preset = presets[Random.Shared.Next(presets.Length)];
            else 
                Log.Warning("Presets directory does not exist or contain any preset");
        }

        string logPrefix = string.IsNullOrEmpty(options.Preset) ? "log" : options.Preset;
        Logging.CreateLogger(logPrefix, IsContentManager, options.Preset, options.UseVerboseLogging);
        
        AppDomain.CurrentDomain.UnhandledException += UnhandledException;
        Log.Information("AssettoServer {Version}", ThisAssembly.AssemblyInformationalVersion);
        if (IsContentManager)
        {
            Log.Debug("Server was started through Content Manager");
        }

        var startOptions = new StartOptions
        {
            Preset = options.Preset,
            ServerCfgPath = options.ServerCfgPath,
            EntryListPath = options.EntryListPath
        };

        if (options.SimulateRace)
        {
            using var simulationCts = new CancellationTokenSource();
            var simulationTask = RunRaceSimulationAsync(startOptions.Preset, startOptions.ServerCfgPath,
                startOptions.EntryListPath, options, simulationCts.Token);
            var shutdownTask = WaitForShutdownFileAsync(options.ShutdownFile, simulationCts.Token);
            if (await Task.WhenAny(simulationTask, shutdownTask) == shutdownTask)
                await simulationCts.CancelAsync();
            await simulationTask;
            await simulationCts.CancelAsync();
            try
            {
                await shutdownTask;
            }
            catch (OperationCanceledException)
            {
            }
            return;
        }
        
        while (true)
        {
            _restartTask = new TaskCompletionSource<StartOptions>();
            using var cts = new CancellationTokenSource();
            var serverTask = RunServerAsync(startOptions.Preset, startOptions.ServerCfgPath,
                startOptions.EntryListPath, startOptions.PortOverrides, options.UseVerboseLogging,
                options.RaceControlDirectory, cts.Token);
            var shutdownTask = WaitForShutdownFileAsync(options.ShutdownFile, cts.Token);
            var finishedTask = await Task.WhenAny(serverTask, _restartTask.Task, shutdownTask);

            if (finishedTask == _restartTask.Task)
            {
                await cts.CancelAsync();
                await serverTask;

                startOptions = _restartTask.Task.Result;
            }
            else if (finishedTask == shutdownTask)
            {
                await cts.CancelAsync();
                await serverTask;
                break;
            }
            else break;
        }
    }

    private static async Task WaitForShutdownFileAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        path = Path.GetFullPath(path);
        while (!File.Exists(path))
        {
            await Task.Delay(250, cancellationToken);
        }

        Log.Information("Shutdown requested by control file");
    }

    private static void PrepareRacePhysics(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.AssettoCorsaRoot)
            || string.IsNullOrWhiteSpace(options.PhysicsTrack)
            || string.IsNullOrWhiteSpace(options.PhysicsCars)
            || string.IsNullOrWhiteSpace(options.PhysicsOutput))
        {
            throw new ArgumentException("--prepare-race-physics requires --ac-root, --track, --cars and --physics-output");
        }

        var result = RacePhysicsAssetBuilder.Build(options.AssettoCorsaRoot, options.PhysicsTrack,
            options.PhysicsTrackConfig, options.PhysicsCars.Split(';', StringSplitOptions.RemoveEmptyEntries),
            options.PhysicsOutput);
        Console.WriteLine($"Prepared rigid-body assets: {result.GridSlots} grid slots, "
                          + $"{result.TrackTriangles} track triangles, {result.CarColliders} car colliders");
    }

    public static void RestartServer(
        string? preset,
        string? serverCfgPath = null,
        string? entryListPath = null,
        PortOverrides? portOverrides = null)
    {
        Log.Information("Initiated in-process server restart");
        _restartTask.SetResult(new StartOptions
        {
            Preset = preset,
            ServerCfgPath = serverCfgPath,
            EntryListPath = entryListPath,
            PortOverrides = portOverrides,
        });
    }

    private static async Task RunServerAsync(
        string? preset,
        string? serverCfgPath,
        string? entryListPath,
        PortOverrides? portOverrides,
        bool useVerboseLogging,
        string? raceControlDirectory,
        CancellationToken token = default)
    {
        ConfigurationLocations = ConfigurationLocations.FromOptions(preset, serverCfgPath, entryListPath);
        
        try
        {
            var config = new ACServerConfiguration(preset, ConfigurationLocations, _loadPluginsFromWorkdir, _generatePluginConfigs, portOverrides);
            var runtimeOptions = ServerRuntimeOptions.CreateLiveServer(raceControlDirectory);

            string logPrefix = string.IsNullOrEmpty(preset) ? "log" : preset;
            Logging.CreateLogger(logPrefix, IsContentManager, preset, useVerboseLogging, config.Extra.RedactIpAddresses, config.Extra.LokiSettings);

            if (!string.IsNullOrEmpty(preset))
            {
                Log.Information("Using preset {Preset}", preset);
            }

            var host = Host.CreateDefaultBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .UseSerilog()
                .ConfigureAppConfiguration(builder => { builder.Sources.Clear(); })
                .ConfigureWebHostDefaults(webHostBuilder =>
                {
                    webHostBuilder.ConfigureKestrel(o => o.ConfigureEndpointDefaults(lo =>
                            lo.ApplicationServices
                                .GetServices<Func<ConnectionDelegate, ConnectionDelegate>>()
                                .ForEach(m => lo.Use(m))))
                        .UseStartup(_ => new Startup(config, runtimeOptions))
                        .UseUrls($"http://{config.Extra.NetworkBindAddress}:{config.Server.HttpPort}");
                })
                .Build();

            var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var stoppedRegistration = applicationLifetime.ApplicationStopped
                .Register(() => OnApplicationStopped(applicationLifetime, host.Services.GetServices<IHostedService>()));

            await host.RunAsync(token);
            await stoppedRegistration.DisposeAsync();
        }
        catch (Exception ex)
        {
            CrashReportHelper.HandleFatalException(ex);
        }
    }

    private static async Task RunRaceSimulationAsync(string? preset, string? serverCfgPath,
        string? entryListPath, Options options, CancellationToken token)
    {
        ConfigurationLocations = ConfigurationLocations.FromOptions(preset, serverCfgPath, entryListPath);
        try
        {
            var config = new ACServerConfiguration(preset, ConfigurationLocations,
                _loadPluginsFromWorkdir, _generatePluginConfigs, null);
            if (!config.Extra.EnableAi || config.Extra.AiParams.Behavior != AiBehaviorMode.Race)
                throw new ConfigurationException("--simulate-race requires EnableAi: true and AiParams Behavior: Race");
            if (!config.Sessions.Any(session => session.Type == Shared.Model.SessionType.Race))
                throw new ConfigurationException("--simulate-race requires a race session");
            if (config.EntryList.Cars.Count(car => car.AiMode != Server.AiMode.None) < 2)
                throw new ConfigurationException("--simulate-race requires at least two bot-capable entries");

            var runtimeOptions = ServerRuntimeOptions.CreateSimulation(options.SimulationOutput,
                options.SimulationSeed, options.SimulationMaximumMinutes,
                options.SimulationMaximumWallSeconds, options.SimulationSampleMilliseconds,
                options.RaceControlDirectory);
            string logPrefix = string.IsNullOrEmpty(preset) ? "simulation" : $"{preset}-simulation";
            Logging.CreateLogger(logPrefix, false, preset, options.UseVerboseLogging,
                config.Extra.RedactIpAddresses, config.Extra.LokiSettings);
            Log.Information("Running network-free race simulation for preset {Preset}", preset);

            var startup = new Startup(config, runtimeOptions);
            using var host = Host.CreateDefaultBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .UseSerilog()
                .ConfigureAppConfiguration(builder => builder.Sources.Clear())
                .ConfigureServices(services => services.Configure<HostOptions>(hostOptions =>
                {
                    hostOptions.ShutdownTimeout = TimeSpan.FromSeconds(15);
                    hostOptions.ServicesStartConcurrently = false;
                    hostOptions.ServicesStopConcurrently = false;
                }))
                .ConfigureContainer<ContainerBuilder>(startup.ConfigureContainer)
                .Build();
            await host.RunAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Log.Fatal(ex, "Race simulation failed to start");
            Console.Error.WriteLine(ex);
        }
    }
    
    // This handles all exceptions thrown in BackgroundService.ExecuteAsync after the first await
    private static void OnApplicationStopped(IHostApplicationLifetime applicationLifetime, IEnumerable<IHostedService> services)
    {
        var exceptions = new List<Exception>();
        foreach (var service in services)
        {
            if (service is not BackgroundService backgroundService) continue;
            var backgroundTask = backgroundService.ExecuteTask;
            if (backgroundTask == null) continue;
            var aggregateException = backgroundTask.Exception;
            if (aggregateException == null) continue;
            
            if (applicationLifetime.ApplicationStopping.IsCancellationRequested
                && backgroundTask.IsCanceled
                && aggregateException.InnerExceptions.All(e => e is TaskCanceledException))
            {
                continue;
            }
            
            exceptions.AddRange(aggregateException.InnerExceptions);
        }
        
        if (exceptions.Count == 0) return;
        
        var exception = exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
        CrashReportHelper.HandleFatalException(exception);
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        CrashReportHelper.HandleFatalException((Exception)args.ExceptionObject);
    }

    private static void SetupFluentValidation()
    {
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
        {
            foreach (var attr in member!.GetCustomAttributes(true))
            {
                if (attr is IniFieldAttribute iniAttr)
                {
                    return iniAttr.Key;
                }
            }
            return member.Name;
        };
    }

    private static void SetupMetrics()
    {
        Metrics.ConfigureMeterAdapter(adapterOptions =>
        {
            // Disable a bunch of verbose / unnecessary default metrics
            adapterOptions.InstrumentFilterPredicate = inst => 
                inst.Name != "kestrel.active_connections" 
                && inst.Name != "http.server.active_requests"
                && inst.Name != "kestrel.queued_connections"
                && inst.Name != "http.server.request.duration"
                && inst.Name != "kestrel.connection.duration"
                && inst.Name != "aspnetcore.routing.match_attempts"
                && inst.Name != "dns.lookups.duration"
                && !inst.Name.StartsWith("http.client.");
        });
    }

    private static void DetectContentManager()
    {
        try
        {
            var parentId = Process.GetCurrentProcess().GetParentProcessId();
            IsContentManager = Process.GetProcessById(parentId).ProcessName == "Content Manager";
        }
        catch (Exception)
        {
            // ignored
        }
    }
}
