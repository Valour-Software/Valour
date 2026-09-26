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

        return IsSignalRStopRace(exception);
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
