using System.Diagnostics;

namespace Valour.Client.Photino;

/// <summary>Opens web pages in the person's default browser.</summary>
public static class SystemBrowser
{
    public static Task OpenAsync(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeMailto)
            return Task.CompletedTask;

        var opener = OperatingSystem.IsMacOS() ? "open"
            : OperatingSystem.IsWindows() ? null
            : "xdg-open";

        var start = opener is null
            ? new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }
            : new ProcessStartInfo(opener, [uri.AbsoluteUri]) { UseShellExecute = false };

        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open {uri}: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}
