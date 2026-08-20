using System.Diagnostics;

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
        if (_process is null || _process.HasExited)
        {
            ChangeState(ServerProcessState.Stopped);
            return;
        }

        ChangeState(ServerProcessState.Stopping);
        await File.WriteAllTextAsync(shutdownFilePath, "stop");
        var wait = _process.WaitForExitAsync();
        var completed = await Task.WhenAny(wait, Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));
        if (completed != wait && !_process.HasExited)
        {
            LogReceived?.Invoke(this, "Graceful shutdown timed out; terminating the server process.");
            _process.Kill(true);
            await _process.WaitForExitAsync();
        }
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
        var exitCode = _process?.ExitCode;
        LogReceived?.Invoke(this, $"Server exited{(exitCode is null ? string.Empty : $" with code {exitCode}")}.");
        ChangeState(ServerProcessState.Stopped);
    }

    private void ChangeState(ServerProcessState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_process is { HasExited: true })
        {
            _process.Dispose();
        }
    }
}

public enum ServerProcessState
{
    Stopped,
    Running,
    Stopping,
}
