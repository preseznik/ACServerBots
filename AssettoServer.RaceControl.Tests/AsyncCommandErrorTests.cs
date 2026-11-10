using System.Reflection;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Infrastructure;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

[NonParallelizable]
public sealed class AsyncCommandErrorTests
{
    private Action<Exception>? _previousHandler;

    [SetUp]
    public void SaveHandler() => _previousHandler = AsyncRelayCommand.ErrorHandler;

    [TearDown]
    public void RestoreHandler() => AsyncRelayCommand.ErrorHandler = _previousHandler;

    [Test]
    public async Task FailedCommand_ReportsErrorAndCanRunAgain()
    {
        var reported = new List<Exception>();
        AsyncRelayCommand.ErrorHandler = reported.Add;
        var runs = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            runs++;
            await Task.Yield();
            throw new IOException("results.json is locked");
        });

        await command.ExecuteAsync();
        await command.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.EqualTo(2));
            Assert.That(reported.Select(error => error.Message),
                Is.EqualTo(new[] { "results.json is locked", "results.json is locked" }));
            Assert.That(command.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task CancelledCommand_IsNotReported()
    {
        var reported = new List<Exception>();
        AsyncRelayCommand.ErrorHandler = reported.Add;
        var command = new AsyncRelayCommand(() => Task.FromCanceled(new CancellationToken(true)));

        await command.ExecuteAsync();

        Assert.That(reported, Is.Empty);
    }

    [Test]
    public void FatalError_IsNotSwallowed()
    {
        var reported = new List<Exception>();
        AsyncRelayCommand.ErrorHandler = reported.Add;
        var command = new AsyncRelayCommand(() => throw new InvalidProgramException());

        Assert.ThrowsAsync<InvalidProgramException>(() => command.ExecuteAsync());
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty);
            Assert.That(command.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void WithoutHandler_ErrorPropagatesAsBefore()
    {
        AsyncRelayCommand.ErrorHandler = null;
        var command = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));

        Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());
    }

    [Test]
    public void Policy_SeparatesFatalAndCancellationFromOrdinaryErrors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UnhandledErrorPolicy.IsFatal(new IOException()), Is.False);
            Assert.That(UnhandledErrorPolicy.IsFatal(new InvalidOperationException()), Is.False);
            Assert.That(UnhandledErrorPolicy.IsFatal(new OutOfMemoryException()), Is.True);
            Assert.That(UnhandledErrorPolicy.IsFatal(
                new TargetInvocationException(new AccessViolationException())), Is.True);
            Assert.That(UnhandledErrorPolicy.IsFatal(
                new AggregateException(new IOException(), new OutOfMemoryException())), Is.True);
            Assert.That(UnhandledErrorPolicy.IsCancellation(new TaskCanceledException()), Is.True);
            Assert.That(UnhandledErrorPolicy.IsCancellation(
                new AggregateException(new OperationCanceledException())), Is.True);
            Assert.That(UnhandledErrorPolicy.IsCancellation(
                new AggregateException(new OperationCanceledException(), new IOException())), Is.False);
            Assert.That(UnhandledErrorPolicy.IsCancellation(new IOException()), Is.False);
        });
    }
}
