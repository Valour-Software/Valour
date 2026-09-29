using Valour.Shared.Utilities;

namespace Valour.Client;

public static class SentryGate
{
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// Returns true for framework errors that are known to be harmless and that
    /// application code cannot observe, so hosts can leave them out of reports.
    /// </summary>
    public static bool IsKnownFrameworkNoise(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            var inner = aggregate.Flatten().InnerExceptions;
            return inner.Count > 0 && inner.All(IsKnownFrameworkNoise);
        }

        return IsSignalRStopRace(exception) ||
               FrameworkNoise.IsSignalRClosedWriteFault(exception) ||
               IsWindowsTitleBarFault(exception);
    }

    /// <summary>
    /// Returns true for the E_INVALIDARG that MAUI's Windows window procedure
    /// throws when it asks for the AppWindow of a window that is being torn
    /// down while updating title bar visibility. The Windows host marks this
    /// error handled, and the title bar keeps its current state.
    /// </summary>
    public static bool IsWindowsTitleBarFault(Exception? exception)
    {
        const int E_INVALIDARG = unchecked((int)0x80070057);

        return exception is ArgumentException { HResult: E_INVALIDARG } &&
               exception.StackTrace?.Contains("NavigationRootManager.SetTitleBarVisibility", StringComparison.Ordinal) == true;
    }

    // SignalR's WebSockets transport disposes its stop token source when it is
    // stopped. With stateful reconnect enabled, a stop that overlaps a
    // transport-level reconnect can dispose the token source that the new
    // socket loop still uses. That loop then throws from CancelAfter on a task
    // nothing awaits. The connection is already being stopped, so the error has
    // no effect. Valour code does not call CancelAfter, so an error from it
    // without Valour frames comes from the framework.
    private static bool IsSignalRStopRace(Exception exception)
    {
        if (exception is not ObjectDisposedException)
            return false;

        var trace = exception.StackTrace;
        return trace is not null &&
               trace.Contains("CancellationTokenSource.CancelAfter", StringComparison.Ordinal) &&
               !trace.Contains("Valour.", StringComparison.Ordinal);
    }
}
