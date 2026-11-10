using System.Windows.Threading;
using AssettoServer.RaceControl.Core.Web;
using AssettoServer.RaceControl.ViewModels;

namespace AssettoServer.RaceControl.Web;

public sealed class DispatcherRaceControlWebControl(
    MainViewModel viewModel, Dispatcher dispatcher) : IRaceControlWebControl
{
    public RaceControlWebControlState GetState() => dispatcher.CheckAccess()
        ? viewModel.GetWebControlState()
        : dispatcher.Invoke(viewModel.GetWebControlState);

    public WebOverview? GetOverview()
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return null;
        return dispatcher.CheckAccess() ? viewModel.GetWebOverview() : dispatcher.Invoke(viewModel.GetWebOverview);
    }

    public string? GetMediaPath(string key)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return null;
        return dispatcher.CheckAccess() ? viewModel.GetWebMediaPath(key) : dispatcher.Invoke(() => viewModel.GetWebMediaPath(key));
    }

    public PlayerEventInfo? GetPlayerEvent()
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return null;
        return dispatcher.CheckAccess() ? viewModel.GetPlayerEvent() : dispatcher.Invoke(viewModel.GetPlayerEvent);
    }

    public async Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
        CancellationToken cancellationToken = default)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return RaceControlWebActionResult.Rejected("Race Control is shutting down.");
        if (dispatcher.CheckAccess())
            return await viewModel.ExecuteWebActionAsync(action, cancellationToken);

        Task<RaceControlWebActionResult> actionTask = await dispatcher.InvokeAsync(
            () => viewModel.ExecuteWebActionAsync(action, cancellationToken),
            DispatcherPriority.Normal, cancellationToken);
        return await actionTask;
    }

    public async Task<RaceControlWebActionResult> SetEnvironmentAsync(
        RaceControlWebEnvironmentRequest request, CancellationToken cancellationToken = default)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return RaceControlWebActionResult.Rejected("Race Control is shutting down.");
        if (dispatcher.CheckAccess())
            return await viewModel.SetWebEnvironmentAsync(request, cancellationToken);

        Task<RaceControlWebActionResult> actionTask = await dispatcher.InvokeAsync(
            () => viewModel.SetWebEnvironmentAsync(request, cancellationToken),
            DispatcherPriority.Normal, cancellationToken);
        return await actionTask;
    }
}
