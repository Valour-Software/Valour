namespace Valour.Shared.Hosting;

/// <summary>
/// Central source of truth for the hosts a Valour deployment is served from.
/// Defaults to production. The backend overrides these from HostingConfig at
/// startup so domains are not hardcoded across shared models, the database
/// layer, and server services. (The client will set these from the instance
/// manifest.)
/// </summary>
public static class ValourHosts
{
    /// <summary>
    /// The root domain, e.g. "valour.gg".
    /// </summary>
    public static string RootDomain { get; set; } = "valour.gg";

    /// <summary>
    /// Host serving the web app, e.g. "app.valour.gg".
    /// </summary>
    public static string AppHost { get; set; } = "app.valour.gg";

    /// <summary>
    /// Host serving the public server-rendered thread pages, e.g. "threads.valour.gg".
    /// </summary>
    public static string ThreadsHost { get; set; } = "threads.valour.gg";

    /// <summary>
    /// Host serving proxied/authenticated content, e.g. "cdn.valour.gg".
    /// </summary>
    public static string ContentCdnHost { get; set; } = "cdn.valour.gg";

    /// <summary>
    /// Host serving public assets (avatars, icons, emojis, themes), e.g. "public-cdn.valour.gg".
    /// </summary>
    public static string PublicCdnHost { get; set; } = "public-cdn.valour.gg";

    public static string AppBaseUrl => $"https://{AppHost}";
    public static string ThreadsBaseUrl => $"https://{ThreadsHost}";
    public static string ContentCdnBaseUrl => $"https://{ContentCdnHost}";
    public static string PublicCdnBaseUrl => $"https://{PublicCdnHost}";

    /// <summary>
    /// True if the host is one of this deployment's own web hosts
    /// (root, www, app, or threads).
    /// </summary>
    public static bool IsSelfHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Equals(RootDomain, StringComparison.OrdinalIgnoreCase) ||
               host.Equals($"www.{RootDomain}", StringComparison.OrdinalIgnoreCase) ||
               host.Equals(AppHost, StringComparison.OrdinalIgnoreCase) ||
               host.Equals(ThreadsHost, StringComparison.OrdinalIgnoreCase);
    }
}
