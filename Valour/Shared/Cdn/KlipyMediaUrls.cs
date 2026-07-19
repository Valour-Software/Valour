namespace Valour.Shared.Cdn;

/// <summary>
/// Validation for media URLs returned by Klipy. Keep this intentionally narrow:
/// message attachments must never accept arbitrary third-party hosts.
/// </summary>
public static class KlipyMediaUrls
{
    public const string MediaHost = "media.klipy.com";

    public static bool IsAllowed(string? location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.Host.Equals(MediaHost, StringComparison.OrdinalIgnoreCase);
    }
}
