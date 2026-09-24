using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Valour.Server.Utilities;

/// <summary>
/// Browser security headers for every response this server sends. HTML responses
/// get a Content-Security-Policy and refuse to be framed: the Blazor app shell gets
/// the app policy and the public Razor pages (threads, wiki, planet info) get a
/// smaller nonce-based policy. The Cloudflare Pages build (cf-build) writes the same
/// app policy into _headers, so keep the two in sync.
/// </summary>
public static partial class SecurityHeaders
{
    public const string AppShellPath = "_content/Valour.Client/index.html";

    private const string NonceItemKey = "Valour.CspNonce";

    // Third-party scripts the app loads: libraries injected by components
    // (highlight.js, cropper, pickr, emoji-mart, lottie-player, TradingView) and
    // the message embed widgets (main.js trustedEmbedScriptHosts), plus the hosts
    // those widgets load their own code from.
    private static readonly string[] ThirdPartyScriptSources =
    [
        "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.2.0/",
        "https://cdnjs.cloudflare.com/ajax/libs/cropperjs/1.5.13/",
        "https://cdn.jsdelivr.net/npm/@simonwep/pickr/",
        "https://cdn.jsdelivr.net/npm/emoji-mart@5.6.0/",
        "https://unpkg.com/@lottiefiles/lottie-player@2.0.12/",
        "https://s3.tradingview.com",
        "https://platform.twitter.com",
        "https://cdn.syndication.twimg.com",
        "https://embed.reddit.com",
        "https://www.tiktok.com",
        "https://*.tiktokcdn.com",
        "https://*.tiktokcdn-us.com",
        "https://*.ttwstatic.com",
        "https://gist.github.com",
    ];

    // Iframe hosts for message embeds: main.js trustedEmbedIframeHosts, the
    // attachment components' players, and the frames the embed widgets create.
    private static readonly string[] EmbedFrameSources =
    [
        "https://www.youtube.com",
        "https://youtube.com",
        "https://music.youtube.com",
        "https://player.vimeo.com",
        "https://vimeo.com",
        "https://player.twitch.tv",
        "https://clips.twitch.tv",
        "https://www.tiktok.com",
        "https://platform.twitter.com",
        "https://syndication.twitter.com",
        "https://twitter.com",
        "https://www.instagram.com",
        "https://embed.bsky.app",
        "https://open.spotify.com",
        "https://w.soundcloud.com",
        "https://embed.reddit.com",
        "https://s.tradingview.com",
        "https://www.tradingview-widget.com",
    ];

    // Players that Markdig's media link extension emits on public wiki and thread pages.
    private static readonly string[] PublicPageFrameSources =
    [
        "https://www.youtube.com",
        "https://player.vimeo.com",
        "https://music.yandex.ru",
        "https://ok.ru",
    ];

    /// <summary>
    /// Builds the policy for the Blazor app shell. Scripts run only from this origin,
    /// the listed third-party hosts, and index.html's inline scripts (allowed by the
    /// hashes in <paramref name="inlineScriptHashes"/>). Inline event handler
    /// attributes, javascript: URLs, and eval are blocked; 'wasm-unsafe-eval' only
    /// lets the .NET runtime compile WebAssembly. Community nodes and voice servers
    /// live on arbitrary domains, so connections are allowed to any https/wss origin;
    /// user media (avatars, attachments, link previews) can come from any https host.
    /// </summary>
    public static string BuildAppPolicy(IEnumerable<string> inlineScriptHashes, bool isDevelopment)
    {
        var scriptSources = new List<string> { "'self'", "'wasm-unsafe-eval'" };
        scriptSources.AddRange(inlineScriptHashes.Select(h => $"'{h}'"));
        scriptSources.AddRange(ThirdPartyScriptSources);

        var insecureConnect = isDevelopment ? " http: ws:" : string.Empty;

        return string.Join("; ",
            "default-src 'self'",
            $"script-src {string.Join(' ', scriptSources)}",
            "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://github.githubassets.com",
            "img-src 'self' data: blob: https:",
            "media-src 'self' data: blob: https:",
            "font-src 'self' data: https:",
            $"connect-src 'self' https: wss:{insecureConnect}",
            "worker-src 'self' blob:",
            $"frame-src {string.Join(' ', EmbedFrameSources)}",
            "manifest-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }

    /// <summary>
    /// Builds the policy for the public Razor pages. They load their own styles and
    /// scripts, highlight.js from cdnjs (wiki pages), and inline scripts that carry
    /// the per-request nonce from <see cref="GetNonce"/>. Inline event handlers are
    /// never allowed here, since these pages render user-authored markdown.
    /// </summary>
    public static string BuildPublicPagePolicy(string nonce)
    {
        var nonceSource = nonce is null ? string.Empty : $" 'nonce-{nonce}'";

        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.2.0/{nonceSource}",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data: https:",
            "media-src 'self' https:",
            "font-src 'self' data: https:",
            "connect-src 'self'",
            $"frame-src {string.Join(' ', PublicPageFrameSources)}",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'");
    }

    /// <summary>
    /// Returns the CSP nonce for this request, creating it on first use. Razor pages
    /// put it on inline script tags; the header middleware adds it to the policy.
    /// </summary>
    public static string GetNonce(HttpContext context)
    {
        if (context.Items.TryGetValue(NonceItemKey, out var existing) && existing is string nonce)
            return nonce;

        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItemKey] = nonce;
        return nonce;
    }

    [GeneratedRegex(@"<script(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    /// <summary>
    /// Computes CSP source hashes for the inline executable scripts in an HTML
    /// document, so the served page can run them without allowing 'unsafe-inline'.
    /// </summary>
    public static List<string> ComputeInlineScriptHashes(string html)
    {
        var hashes = new List<string>();
        foreach (Match match in ScriptTagRegex().Matches(html))
        {
            var attrs = match.Groups["attrs"].Value;
            if (attrs.Contains("src=", StringComparison.OrdinalIgnoreCase))
                continue;

            // Browsers hash the script text after HTML newline normalization,
            // which turns CRLF and lone CR into LF (the checkout uses CRLF).
            var body = match.Groups["body"].Value.Replace("\r\n", "\n").Replace('\r', '\n');
            var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body));
            hashes.Add("sha256-" + Convert.ToBase64String(digest));
        }

        return hashes;
    }

    /// <summary>
    /// Adds the security header middleware. Call before static files and routing so
    /// every response, including static assets and the SPA fallback, is covered.
    /// </summary>
    public static void UseValourSecurityHeaders(this WebApplication app)
    {
        var appShellHashes = new List<string>();
        var appShell = app.Environment.WebRootFileProvider.GetFileInfo(AppShellPath);
        if (appShell.Exists)
        {
            using var stream = appShell.CreateReadStream();
            using var reader = new StreamReader(stream);
            appShellHashes = ComputeInlineScriptHashes(reader.ReadToEnd());
        }
        else
        {
            app.Logger.LogWarning("App shell {Path} not found; the app CSP will block its inline scripts", AppShellPath);
        }

        var appPolicy = BuildAppPolicy(appShellHashes, app.Environment.IsDevelopment());

        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context, appPolicy);
                return Task.CompletedTask;
            });

            return next();
        });
    }

    private static void Apply(HttpContext context, string appPolicy)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        if (!headers.ContainsKey("Referrer-Policy"))
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        var contentType = context.Response.ContentType;
        if (contentType is null || !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            return;

        headers.XFrameOptions = "DENY";

        if (headers.ContainsKey("Content-Security-Policy"))
            return;

        // Swagger UI relies on inline scripts; only forbid framing it.
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            return;
        }

        var isRazorPage = context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is not null;
        if (isRazorPage)
        {
            context.Items.TryGetValue(NonceItemKey, out var nonce);
            headers.ContentSecurityPolicy = BuildPublicPagePolicy(nonce as string);
            return;
        }

        headers.ContentSecurityPolicy = appPolicy;
    }
}
