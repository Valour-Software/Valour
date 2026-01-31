using System.Text.RegularExpressions;
using System.Web;

namespace Valour.Server.Utilities;

/// <summary>
/// Sanitizes oEmbed HTML content to prevent XSS attacks.
/// Allows only safe HTML elements and attributes from trusted oEmbed providers.
/// </summary>
public static class OEmbedSanitizer
{
    /// <summary>
    /// Allowed HTML tags for oEmbed content
    /// </summary>
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockquote", "a", "p", "br", "div", "span", "img", "iframe",
        "strong", "em", "b", "i", "u", "time", "cite"
    };

    /// <summary>
    /// Allowed attributes for all tags
    /// </summary>
    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "class", "id", "data-instgrm-captioned",
        "data-instgrm-permalink", "data-instgrm-version", "datetime",
        "width", "height", "frameborder", "allowfullscreen", "allow",
        "data-tweet-id", "data-embed-theme", "cite", "data-conversation",
        "data-lang", "data-dnt", "data-theme", "data-width", "data-height"
    };

    /// <summary>
    /// Trusted domains for script sources
    /// </summary>
    private static readonly HashSet<string> TrustedScriptDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "platform.twitter.com",
        "embed.reddit.com",
        "www.instagram.com",
        "platform.instagram.com",
        "connect.facebook.net",
        "www.tiktok.com",
        "embed.bsky.app",
        "w.soundcloud.com",
    };

    /// <summary>
    /// Trusted domains for iframe sources
    /// </summary>
    private static readonly HashSet<string> TrustedIframeDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video platforms
        "www.youtube.com",
        "youtube.com",
        "music.youtube.com",
        "player.vimeo.com",
        "vimeo.com",
        "player.twitch.tv",
        "clips.twitch.tv",
        "www.tiktok.com",

        // Social platforms
        "platform.twitter.com",
        "twitter.com",
        "www.instagram.com",
        "embed.bsky.app",

        // Music platforms
        "open.spotify.com",
        "w.soundcloud.com",

        // Developer platforms
        "gist.github.com",
    };

    /// <summary>
    /// Dangerous event handler attributes that must be removed
    /// </summary>
    private static readonly Regex EventHandlerPattern = new(
        @"\s+on\w+\s*=\s*[""'][^""']*[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to match javascript: URLs
    /// </summary>
    private static readonly Regex JavaScriptUrlPattern = new(
        @"(?:href|src)\s*=\s*[""']?\s*javascript:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to match data: URLs (except safe image types)
    /// </summary>
    private static readonly Regex DataUrlPattern = new(
        @"(?:href|src)\s*=\s*[""']?\s*data:(?!image/(?:png|jpg|jpeg|gif|webp))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to extract script src values
    /// </summary>
    private static readonly Regex ScriptSrcPattern = new(
        @"<script[^>]*\ssrc\s*=\s*[""']([^""']+)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to extract iframe src values
    /// </summary>
    private static readonly Regex IframeSrcPattern = new(
        @"<iframe[^>]*\ssrc\s*=\s*[""']([^""']+)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to match inline scripts (script tags without src)
    /// </summary>
    private static readonly Regex InlineScriptPattern = new(
        @"<script(?![^>]*\ssrc\s*=)[^>]*>[\s\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes oEmbed HTML content from a trusted provider.
    /// </summary>
    /// <param name="html">The raw HTML from the oEmbed provider</param>
    /// <returns>Sanitized HTML safe for rendering, or null if content is deemed unsafe</returns>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        // Remove any javascript: URLs
        if (JavaScriptUrlPattern.IsMatch(html))
        {
            html = JavaScriptUrlPattern.Replace(html, "href=\"#\"");
        }

        // Remove dangerous data: URLs (keep safe image types)
        if (DataUrlPattern.IsMatch(html))
        {
            html = DataUrlPattern.Replace(html, "src=\"\"");
        }

        // Remove all event handlers (onclick, onerror, onload, etc.)
        html = EventHandlerPattern.Replace(html, "");

        // Remove inline scripts (no src attribute)
        html = InlineScriptPattern.Replace(html, "");

        // Validate script sources - only allow from trusted domains
        var scriptMatches = ScriptSrcPattern.Matches(html);
        foreach (Match match in scriptMatches)
        {
            var src = match.Groups[1].Value;
            if (!IsScriptSourceTrusted(src))
            {
                // Remove untrusted script tags
                html = html.Replace(match.Value, "<!-- script removed -->");
            }
        }

        // Validate iframe sources - only allow from trusted domains
        var iframeMatches = IframeSrcPattern.Matches(html);
        foreach (Match match in iframeMatches)
        {
            var src = match.Groups[1].Value;
            if (!IsIframeSourceTrusted(src))
            {
                // Remove untrusted iframe tags
                html = html.Replace(match.Value, "<!-- iframe removed -->");
            }
        }

        // Remove style attributes that could contain expressions
        html = Regex.Replace(html, @"\sstyle\s*=\s*[""'][^""']*expression[^""']*[""']", "", RegexOptions.IgnoreCase);

        return html;
    }

    /// <summary>
    /// Checks if a script source URL is from a trusted domain
    /// </summary>
    private static bool IsScriptSourceTrusted(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return false;

        try
        {
            var uri = new Uri(src, UriKind.RelativeOrAbsolute);
            if (!uri.IsAbsoluteUri)
                return false;

            // Must be HTTPS
            if (uri.Scheme != "https")
                return false;

            return TrustedScriptDomains.Contains(uri.Host);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if an iframe source URL is from a trusted domain
    /// </summary>
    private static bool IsIframeSourceTrusted(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return false;

        try
        {
            var uri = new Uri(src, UriKind.RelativeOrAbsolute);
            if (!uri.IsAbsoluteUri)
                return false;

            // Must be HTTPS
            if (uri.Scheme != "https")
                return false;

            return TrustedIframeDomains.Contains(uri.Host);
        }
        catch
        {
            return false;
        }
    }
}
