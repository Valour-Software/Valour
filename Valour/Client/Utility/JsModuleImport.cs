using Microsoft.JSInterop;

namespace Valour.Client.Utility;

/// <summary>
/// Imports JavaScript modules for components. A module fetch can fail because
/// of a network interruption, or because a client that was loaded before a
/// deployment asks for a file the server no longer has. These helpers retry the
/// import and let optional features continue without their module instead of
/// failing the render.
/// </summary>
public static class JsModuleImport
{
    // Delays before the second and third attempts.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1),
    ];

    /// <summary>
    /// Imports a module, retrying failed fetches. Throws the last
    /// <see cref="JSException"/> when every attempt fails.
    /// </summary>
    /// <remarks>
    /// Some browsers remember a failed module fetch for the lifetime of the
    /// page, so retries add a query parameter to request the file again. A
    /// retried import therefore creates its own module instance.
    /// </remarks>
    public static async Task<IJSObjectReference> ImportModuleAsync(this IJSRuntime js, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await js.InvokeAsync<IJSObjectReference>("import", WithRetryMarker(path, attempt));
            }
            catch (JSException) when (attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt]);
            }
        }
    }

    /// <summary>
    /// Imports a module for an optional feature. Returns null when the module
    /// cannot be loaded, so the caller can leave that feature inactive.
    /// </summary>
    public static async Task<IJSObjectReference?> TryImportModuleAsync(this IJSRuntime js, string path)
    {
        try
        {
            return await js.ImportModuleAsync(path);
        }
        catch (JSException ex)
        {
            Console.WriteLine($"[JsModuleImport] Could not load {path}: {ex.Message}");
            return null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static string WithRetryMarker(string path, int attempt)
    {
        if (attempt == 0)
            return path;

        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}importRetry={attempt}";
    }
}
