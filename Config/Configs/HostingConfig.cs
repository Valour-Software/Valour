namespace Valour.Config.Configs;

/// <summary>
/// Configuration for the public domains Valour is served from. Centralizes the
/// root domain and the subdomains for the app, API, and static thread pages so
/// these are not hardcoded across the backend.
/// </summary>
public class HostingConfig
{
    public static HostingConfig? Current;

    public HostingConfig()
    {
        Current = this;
    }

    /// <summary>
    /// Display name of this instance, shown to clients.
    /// </summary>
    public string InstanceName { get; set; } = "Valour";

    /// <summary>
    /// The root domain, e.g. "valour.gg".
    /// </summary>
    public string RootDomain { get; set; } = "valour.gg";

    /// <summary>
    /// Subdomain serving the Blazor web app (Cloudflare Pages), e.g. "app".
    /// </summary>
    public string AppSubdomain { get; set; } = "app";

    /// <summary>
    /// Subdomain serving the backend API (Kestrel), e.g. "api".
    /// </summary>
    public string ApiSubdomain { get; set; } = "api";

    /// <summary>
    /// Subdomain serving the static server-rendered thread pages, e.g. "threads".
    /// </summary>
    public string ThreadsSubdomain { get; set; } = "threads";

    /// <summary>
    /// Subdomain serving the public server-rendered docs pages, e.g. "docs".
    /// </summary>
    public string WikiSubdomain { get; set; } = "wiki";

    /// <summary>
    /// Subdomain serving proxied/authenticated content, e.g. "cdn".
    /// </summary>
    public string ContentCdnSubdomain { get; set; } = "cdn";

    /// <summary>
    /// Subdomain serving public assets, e.g. "public-cdn".
    /// </summary>
    public string PublicCdnSubdomain { get; set; } = "public-cdn";

    private static string Combine(string subdomain, string root) =>
        string.IsNullOrWhiteSpace(subdomain) ? root : $"{subdomain}.{root}";

    public string AppHost => Combine(AppSubdomain, RootDomain);
    public string ApiHost => Combine(ApiSubdomain, RootDomain);
    public string ThreadsHost => Combine(ThreadsSubdomain, RootDomain);
    public string WikiHost => Combine(WikiSubdomain, RootDomain);
    public string ContentCdnHost => Combine(ContentCdnSubdomain, RootDomain);
    public string PublicCdnHost => Combine(PublicCdnSubdomain, RootDomain);

    public string AppBaseUrl => $"https://{AppHost}";
    public string ApiBaseUrl => $"https://{ApiHost}";
    public string ThreadsBaseUrl => $"https://{ThreadsHost}";
    public string WikiBaseUrl => $"https://{WikiHost}";
}
