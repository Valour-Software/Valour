using Microsoft.JSInterop;
using Valour.Sdk.E2ee;

namespace Valour.Client.Blazor;

/// <summary>
/// Keeps notification keys in IndexedDB, where the service worker reads them
/// to show message text in push notifications. See notification-preview.js.
/// </summary>
public sealed class BrowserNotificationKeyStore : INotificationKeyStore
{
    private readonly IJSRuntime _js;
    private Task _loaded;

    public BrowserNotificationKeyStore(IJSRuntime js)
    {
        _js = js;
    }

    // The script is a classic script shared with the service worker. Importing
    // it runs it once, which defines ValourNotificationPreview. A failed import
    // is tried again on the next use.
    private async Task EnsureLoadedAsync()
    {
        _loaded ??= _js.InvokeAsync<IJSObjectReference>("import", "./notification-preview.js").AsTask();
        try
        {
            await _loaded;
        }
        catch
        {
            _loaded = null;
            throw;
        }
    }

    public async Task<string> LoadAsync()
    {
        await EnsureLoadedAsync();
        return await _js.InvokeAsync<string>("ValourNotificationPreview.loadKeys");
    }

    public async Task SaveAsync(string keySet)
    {
        await EnsureLoadedAsync();
        await _js.InvokeVoidAsync("ValourNotificationPreview.saveKeys", keySet);
    }

    public async Task ClearAsync()
    {
        await EnsureLoadedAsync();
        await _js.InvokeVoidAsync("ValourNotificationPreview.clearKeys");
    }
}
