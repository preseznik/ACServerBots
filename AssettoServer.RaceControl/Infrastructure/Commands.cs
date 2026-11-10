using System.Windows.Input;
using AssettoServer.RaceControl.Core.Infrastructure;

namespace AssettoServer.RaceControl.Infrastructure;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _isRunning;

    /// <summary>
    /// Receives non-fatal errors from every async command so one failed action doesn't close the app.
    /// The app sets it at startup. Without it, errors propagate as before.
    /// </summary>
    public static Action<Exception>? ErrorHandler { get; set; }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception exception) when (UnhandledErrorPolicy.IsCancellation(exception))
        {
            // A cancelled action is not an error.
        }
        catch (Exception exception) when (!UnhandledErrorPolicy.IsFatal(exception) && ErrorHandler is not null)
        {
            ErrorHandler(exception);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
