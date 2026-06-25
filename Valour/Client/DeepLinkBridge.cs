namespace Valour.Client;

/// <summary>
/// Static bridge that carries deep links from native hosts (e.g. an Android push
/// notification tap) into the running Blazor app.
///
/// Native code calls <see cref="Open"/> with a Valour URL or route. If the app
/// is not yet ready to route (cold start from a notification), the link is
/// buffered until the app registers a handler via <see cref="SetHandler"/>.
/// </summary>
public static class DeepLinkBridge
{
    private static readonly object Lock = new();
    private static string _pending;
    private static Func<string, Task> _handler;

    /// <summary>
    /// Invoked by native platform code with a Valour URL or relative route.
    /// Safe to call before the app has finished loading.
    /// </summary>
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        Func<string, Task> handler;
        lock (Lock)
        {
            handler = _handler;
            if (handler is null)
            {
                // App isn't ready yet - hold the most recent link for cold start.
                _pending = url;
                return;
            }
        }

        _ = InvokeHandlerAsync(handler, url);
    }

    /// <summary>
    /// Registers the app's deep-link handler and immediately drains any link that
    /// arrived before the app was ready (cold start). Called once the window dock
    /// is loaded and capable of opening destinations.
    /// </summary>
    public static void SetHandler(Func<string, Task> handler)
    {
        if (handler is null)
            return;

        string pending;
        lock (Lock)
        {
            _handler = handler;
            pending = _pending;
            _pending = null;
        }

        if (pending is not null)
            _ = InvokeHandlerAsync(handler, pending);
    }

    /// <summary>
    /// Clears the handler if it matches the currently registered one.
    /// </summary>
    public static void ClearHandler(Func<string, Task> handler)
    {
        lock (Lock)
        {
            if (ReferenceEquals(_handler, handler))
                _handler = null;
        }
    }

    private static async Task InvokeHandlerAsync(Func<string, Task> handler, string url)
    {
        try
        {
            await handler(url);
        }
        catch
        {
            // A failed deep link should never take down the host.
        }
    }
}
