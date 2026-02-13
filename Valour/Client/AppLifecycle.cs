namespace Valour.Client;

/// <summary>
/// Static event bridge for native app lifecycle events (e.g. Android OnResume)
/// that need to reach Blazor components.
/// </summary>
public static class AppLifecycle
{
    public static event Action? Resumed;

    public static void NotifyResumed() => Resumed?.Invoke();
}
