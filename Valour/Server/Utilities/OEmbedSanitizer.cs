using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Valour.Server.Utilities;

/// <summary>
/// Sanitizes oEmbed HTML content to reduce XSS risk.
/// </summary>
public static class OEmbedSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockquote", "a", "p", "br", "div", "span", "img", "iframe",
        "strong", "em", "b", "i", "u", "time", "cite"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "class", "id", "data-instgrm-captioned",
        "data-instgrm-permalink", "data-instgrm-version", "datetime",
        "width", "height", "frameborder", "allowfullscreen", "allow",
        "data-tweet-id", "data-embed-theme", "cite", "data-conversation",
        "data-lang", "data-dnt", "data-theme", "data-width", "data-height"
    };

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
    };

    private static readonly Regex EventHandlerPattern = new(
        @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JavaScriptUrlPattern = new(
        @"(?:href|src)\s*=\s*[""']?\s*javascript:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DataUrlPattern = new(
        @"(?:href|src)\s*=\s*[""']?\s*data:(?!image/(?:png|jpg|jpeg|gif|webp))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineScriptPattern = new(
        @"<script[^>]*>[\s\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagPattern = new(
        @"<(?<close>/)?(?<tag>[a-zA-Z0-9]+)(?<attrs>[^>]*)>",
        RegexOptions.Compiled);

    private static readonly Regex AttributePattern = new(
        @"(?<name>[^\s=/>]+)(?:\s*=\s*(?<value>""[^""]*""|'[^']*'|[^\s""'=<>`]+))?",
        RegexOptions.Compiled);

    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        html = html.Replace("\0", string.Empty);
        html = EventHandlerPattern.Replace(html, string.Empty);
        html = InlineScriptPattern.Replace(html, string.Empty);
        html = JavaScriptUrlPattern.Replace(html, "href=\"#\"");
        html = DataUrlPattern.Replace(html, "src=\"\"");

        return TagPattern.Replace(html, BuildSanitizedTag);
    }

    private static string BuildSanitizedTag(Match match)
    {
        var tagName = match.Groups["tag"].Value.ToLowerInvariant();
        var isClosingTag = match.Groups["close"].Success;

        if (!AllowedTags.Contains(tagName))
            return string.Empty;

        if (isClosingTag)
            return $"</{tagName}>";

        var attrs = match.Groups["attrs"].Value;
        var sanitizedAttrs = BuildSanitizedAttributes(tagName, attrs);
        var selfClosing = match.Value.EndsWith("/>");

        if (selfClosing)
            return $"<{tagName}{sanitizedAttrs} />";

        return $"<{tagName}{sanitizedAttrs}>";
    }

    private static string BuildSanitizedAttributes(string tagName, string rawAttributes)
    {
        if (string.IsNullOrWhiteSpace(rawAttributes))
            return string.Empty;

        var sb = new StringBuilder();

        foreach (Match attrMatch in AttributePattern.Matches(rawAttributes))
        {
            var name = attrMatch.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == "/" || name.StartsWith("/"))
                continue;

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!AllowedAttributes.Contains(name))
                continue;

            var lowerName = name.ToLowerInvariant();
            var hasValue = attrMatch.Groups["value"].Success;

            if (!hasValue)
            {
                if (lowerName == "allowfullscreen")
                    sb.Append(" allowfullscreen");
                continue;
            }

            var value = TrimQuotes(attrMatch.Groups["value"].Value);
            if (!IsAttributeValueSafe(tagName, lowerName, value))
                continue;

            sb.Append(' ')
                .Append(lowerName)
                .Append("=\"")
                .Append(HttpUtility.HtmlAttributeEncode(value))
                .Append('"');
        }

        return sb.ToString();
    }

    private static bool IsAttributeValueSafe(string tagName, string attrName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Contains('\0'))
            return false;

        if (value.StartsWith("//"))
            return false;

        if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (attrName is "href" or "src" or "cite")
        {
            if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                return false;

            if (uri.IsAbsoluteUri && uri.Scheme is not ("http" or "https"))
                return false;

            if (attrName == "src" && !uri.IsAbsoluteUri)
                return false;

            if (tagName == "iframe" && attrName == "src")
                return IsIframeSourceTrusted(value);
        }

        return true;
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool IsIframeSourceTrusted(string src)
    {
        try
        {
            var uri = new Uri(src, UriKind.RelativeOrAbsolute);
            return uri.IsAbsoluteUri &&
                   uri.Scheme == "https" &&
                   TrustedIframeDomains.Contains(uri.Host);
        }
        catch
        {
            return false;
        }
    }
}
