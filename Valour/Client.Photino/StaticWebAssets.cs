using Microsoft.Extensions.FileProviders;

namespace Valour.Client.Photino;

/// <summary>
/// Serves the page and its assets from wwwroot beside the app. A published app
/// has every asset there. A development build has none; the web view reads
/// the static web assets manifest instead, so an empty provider is used.
/// </summary>
public static class StaticWebAssets
{
    public static IFileProvider CreateFileProvider()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return Directory.Exists(root) ? new PhysicalFileProvider(root) : new NullFileProvider();
    }
}
