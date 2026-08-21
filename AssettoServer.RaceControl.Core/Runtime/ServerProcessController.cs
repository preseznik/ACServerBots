using System.Diagnostics;
using System.ComponentModel;

namespace AssettoServer.RaceControl.Core.Runtime;

public sealed class ServerProcessController : IDisposable
{
    private Process? _process;

    public event EventHandler<string>? LogReceived;
    public event EventHandler<ServerProcessState>? StateChanged;

    public ServerProcessState State { get; private set; } = ServerProcessState.Stopped;
    public int? ProcessId => _process is { HasExited: false } ? _process.Id : null;

    public void Start(string executablePath, string workingDirectory, string presetName, string shutdownFilePath)
    {
        if (_process is { HasExited: false })
        {
            throw new InvalidOperationException("A server is already running.");
        }

        ReleaseExitedProcess();

        if (File.Exists(shutdownFilePath))
        {
            File.Delete(shutdownFilePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--preset");
        startInfo.ArgumentList.Add(presetName);
        startInfo.ArgumentList.Add("--shutdown-file");
        startInfo.ArgumentList.Add(shutdownFilePath);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnOutput;
        _process.Exited += OnExited;
        if (!_process.Start())
        {
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("Could not start AssettoServer.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        ChangeState(ServerProcessState.Running);
        LogReceived?.Invoke(this, $"Server started (PID {_process.Id}).");
    }

    public async Task StopAsync(string shutdownFilePath, TimeSpan? timeout = null)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            ReleaseExitedProcess();
            ChangeState(ServerProcessState.Stopped);
            return;
        }

        ChangeState(ServerProcessState.Stopping);
        await StopProcessAsync(process, shutdownFilePath, timeout ?? TimeSpan.FromSeconds(10),
            "Graceful shutdown timed out; terminating the server process.");
        if (ReferenceEquals(_process, process))
            ReleaseExitedProcess();
        if (State != ServerProcessState.Stopped)
            ChangeState(ServerProcessState.Stopped);
    }

    public async Task<int> StopOrphanedServersAsync(string instancesDirectory, TimeSpan? timeout = null)
    {
        int stopped = 0;
        int? trackedProcessId = ProcessId;
        foreach (var process in Process.GetProcessesByName("AssettoServer"))
        {
            using (process)
            {
                if (process.Id == trackedProcessId || process.HasExited)
                    continue;
                string? executablePath = TryGetExecutablePath(process);
                if (executablePath is null
                    || !IsOwnedServerExecutable(executablePath, instancesDirectory))
                    continue;

                string instanceDirectory = Path.GetDirectoryName(executablePath)
                                           ?? throw new InvalidOperationException(
                                               $"Cannot locate the instance folder for PID {process.Id}.");
                string shutdownFilePath = Path.Combine(instanceDirectory, "shutdown.signal");
                LogReceived?.Invoke(this,
                    $"Stopping orphaned Race Control server PID {process.Id} from {instanceDirectory}.");
                await StopProcessAsync(process, shutdownFilePath,
                    timeout ?? TimeSpan.FromSeconds(10),
                    $"Orphaned server PID {process.Id} did not stop gracefully; terminating its process tree.");
                stopped++;
            }
        }

        if (stopped > 0)
            LogReceived?.Invoke(this, $"Stopped {stopped} orphaned Race Control server process(es).");
        return stopped;
    }

    public async Task RestartAsync(string executablePath, string workingDirectory, string presetName, string shutdownFilePath)
    {
        await StopAsync(shutdownFilePath);
        Start(executablePath, workingDirectory, presetName, shutdownFilePath);
    }

    private void OnOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
        {
            LogReceived?.Invoke(this, args.Data);
        }
    }

    private void OnExited(object? sender, EventArgs args)
    {
        if (sender is not Process exitedProcess)
            return;
        int? exitCode = TryGetExitCode(exitedProcess);
        LogReceived?.Invoke(this, $"Server exited{(exitCode is null ? string.Empty : $" with code {exitCode}")}.");
        if (ReferenceEquals(_process, exitedProcess))
            ChangeState(ServerProcessState.Stopped);
    }

    internal static bool IsOwnedServerExecutable(string executablePath, string instancesDirectory)
    {
        if (!Path.GetFileName(executablePath).Equals("AssettoServer.exe",
                StringComparison.OrdinalIgnoreCase))
            return false;
        string root = Path.GetFullPath(instancesDirectory);
        string candidate = Path.GetFullPath(executablePath);
        string relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private async Task StopProcessAsync(Process process, string shutdownFilePath,
        TimeSpan timeout, string timeoutMessage)
    {
        if (process.HasExited)
            return;
        await File.WriteAllTextAsync(shutdownFilePath, "stop");
        var wait = process.WaitForExitAsync();
        var completed = await Task.WhenAny(wait, Task.Delay(timeout));
        if (completed != wait && !process.HasExited)
        {
            LogReceived?.Invoke(this, timeoutMessage);
            process.Kill(true);
            await process.WaitForExitAsync();
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is Win32Exception
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void ReleaseExitedProcess()
    {
        if (_process is not { HasExited: true } process)
            return;
        process.OutputDataReceived -= OnOutput;
        process.ErrorDataReceived -= OnOutput;
        process.Exited -= OnExited;
        process.Dispose();
        _process = null;
    }

    private void ChangeState(ServerProcessState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_process is { } process)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                    process.WaitForExit(5_000);
                }
                catch (Exception exception) when (exception is Win32Exception
                                                 or InvalidOperationException
                                                 or NotSupportedException)
                {
                    // The application is already closing; the next launch will recover this owned process.
                }
            }
            if (process.HasExited)
                ReleaseExitedProcess();
        }
    }
}

public enum ServerProcessState
{
    Stopped,
    Running,
    Stopping,
}
