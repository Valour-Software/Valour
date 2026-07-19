namespace Valour.Shared.Cdn;

/// <summary>
/// Validation for media URLs returned by Klipy. Keep this intentionally narrow:
/// message attachments must never accept arbitrary third-party hosts. Klipy
/// serves its assets (search results and category previews) from
/// <c>static.klipy.com</c>; <c>media.klipy.com</c> is also a Klipy-owned host.
/// This same check gates both client rendering and server attachment acceptance.
/// </summary>
public static class KlipyMediaUrls
{
    public const string MediaHost = "media.klipy.com";
    public const string StaticHost = "static.klipy.com";

    private static readonly string[] AllowedHosts = { StaticHost, MediaHost };

    public static bool IsAllowed(string? location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var host in AllowedHosts)
        {
            if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
