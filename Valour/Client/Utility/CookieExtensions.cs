using Microsoft.JSInterop;

namespace Valour.Client.Utility;

public static class CookieExtensions
{
    public static async Task SetCookieAsync(this IJSRuntime js, string name, string value, string domain = ".valour.gg", int days = 7)
    {
#if DEBUG
        // Localhost does not play nicely with defined domains
        domain = string.Empty;
#endif

        await js.InvokeVoidAsync("setCookie", name, value, domain, days);
    }

    public static async Task<string> GetCookieAsync(this IJSRuntime js, string name)
    {
        return await js.InvokeAsync<string>("getCookie", name);
    }
    
    public static async Task DeleteCookieAsync(this IJSRuntime js, string name)
    {
        await js.InvokeVoidAsync("deleteCookie", name);
    }
}
