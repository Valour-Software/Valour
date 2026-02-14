namespace Valour.Client;

/// <summary>
/// Static event bridge for native app lifecycle events (e.g. Android OnResume)
/// that need to reach Blazor components.
/// </summary>
public static class AppLifecycle
{
    public static event Action? Resumed;

    /// <summary>
    /// Fired when a voice call begins. Platform projects can subscribe to start
    /// keep-alive mechanisms (e.g. Android foreground service).
    /// </summary>
    public static event Action? CallStarted;

    /// <summary>
    /// Fired when a voice call ends.
    /// </summary>
    public static event Action? CallEnded;

    public static void NotifyResumed() => Resumed?.Invoke();
    public static void NotifyCallStarted() => CallStarted?.Invoke();
    public static void NotifyCallEnded() => CallEnded?.Invoke();
}
