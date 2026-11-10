using System.Reflection;
using System.Runtime.InteropServices;

namespace AssettoServer.RaceControl.Core.Infrastructure;

/// <summary>
/// Decides whether an unexpected error leaves Race Control safe to keep running. Most errors in a
/// button or command (a locked file, a failed network call) only affect that action, and closing the
/// launcher mid-race would cost far more than the error itself.
/// </summary>
public static class UnhandledErrorPolicy
{
    public static bool IsFatal(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = Unwrap(current))
        {
            if (current is OutOfMemoryException
                or StackOverflowException
                or AccessViolationException
                or SEHException
                or InvalidProgramException
                or BadImageFormatException
                or TypeInitializationException)
            {
                return true;
            }

            if (current is AggregateException aggregate
                && aggregate.InnerExceptions.Any(IsFatal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCancellation(Exception exception) => exception switch
    {
        OperationCanceledException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0
                                        && aggregate.InnerExceptions.All(IsCancellation),
        _ => false
    };

    private static Exception? Unwrap(Exception exception) => exception switch
    {
        TargetInvocationException or AggregateException => exception.InnerException,
        _ => null
    };
}
