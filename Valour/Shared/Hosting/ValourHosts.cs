namespace Valour.Shared.Hosting;

/// <summary>
/// Central source of truth for Valour's public CDN hosts. Defaults to production.
/// The backend overrides these from HostingConfig at startup so CDN domains are
/// not hardcoded across shared models, the database layer, and server services.
/// </summary>
public static class ValourHosts
{
    /// <summary>
    /// Host serving proxied/authenticated content, e.g. "cdn.valour.gg".
    /// </summary>
    public static string ContentCdnHost { get; set; } = "cdn.valour.gg";

    /// <summary>
    /// Host serving public assets (avatars, icons, emojis, themes), e.g. "public-cdn.valour.gg".
    /// </summary>
    public static string PublicCdnHost { get; set; } = "public-cdn.valour.gg";

    public static string ContentCdnBaseUrl => $"https://{ContentCdnHost}";
    public static string PublicCdnBaseUrl => $"https://{PublicCdnHost}";
}
