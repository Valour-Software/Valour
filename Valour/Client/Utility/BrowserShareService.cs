using Microsoft.JSInterop;

namespace Valour.Client.Utility;

/// <summary>
/// Uses the Web Share API (navigator.share) where available - mobile browsers
/// and installed PWAs. Returns false on desktop browsers without share support
/// so callers can fall back to copying the link.
/// </summary>
public class BrowserShareService : IShareService
{
    private readonly IJSRuntime _js;

    public BrowserShareService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<bool> ShareAsync(string title, string text, string url)
    {
        try
        {
            return await _js.InvokeAsync<bool>("valourShare", title, text, url);
        }
        catch
        {
            return false;
        }
    }
}
