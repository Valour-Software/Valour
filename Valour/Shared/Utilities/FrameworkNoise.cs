namespace Valour.Shared.Utilities;

/// <summary>
/// Recognizes framework errors that are known to be harmless and that
/// application code cannot observe, so hosts can leave them out of error reports.
/// </summary>
public static class FrameworkNoise
{
    /// <summary>
    /// Returns true when a SignalR connection fails to write because the
    /// connection is already closing.
    /// </summary>
    /// <remarks>
    /// With stateful reconnect enabled, SignalR keeps a message buffer whose
    /// timer periodically writes acknowledgement messages. When the connection
    /// closes, the transport completes its output pipe, and a timer tick or a
    /// pending write that races the close throws from the completed pipe on a
    /// task nothing awaits. The connection is already gone, so the error has no
    /// effect. Valour code awaits its own hub calls, so an error from these
    /// paths without Valour frames comes from the framework.
    /// </remarks>
    public static bool IsSignalRClosedWriteFault(Exception exception)
    {
        if (exception is not (InvalidOperationException or ArgumentOutOfRangeException or OperationCanceledException))
            return false;

        var trace = exception.StackTrace;
        if (trace is null || trace.Contains("Valour.", StringComparison.Ordinal))
            return false;

        // Browser WebAssembly traces stop at the hub protocol writer and do not
        // include the message buffer frames, so the completed-pipe throw helper
        // together with the hub protocol identifies the same fault.
        return trace.Contains("MessageBuffer.", StringComparison.Ordinal) ||
               (trace.Contains("ThrowInvalidOperationException_NoWritingAllowed", StringComparison.Ordinal) &&
                trace.Contains("HubProtocol.WriteMessage", StringComparison.Ordinal));
    }
}
