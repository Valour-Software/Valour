using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Valour.Shared.Hosting;

namespace Valour.Server.Cdn;

/// <summary>
/// An image a page offers for its preview, checked to be a real image of a
/// usable size. The server serves it through its media proxy.
/// </summary>
public sealed record SitePreviewImage(string Url, int Width, int Height, string Extension, string MimeType);

/// <summary>
/// What a page says about itself for a link preview, with text cleaned and
/// length-capped. Url is the page the preview describes after redirects.
/// </summary>
public sealed record SitePreview(
    string Title,
    string Description,
    string SiteName,
    string Type,
    string Card,
    SitePreviewImage Image,
    string ImageAlt,
    SitePreviewImage Icon);

/// <summary>
/// Reads the metadata a web page publishes for link previews (Open Graph,
/// Twitter card, and plain HTML tags).
///
/// Pages are untrusted, so a fetch is bounded in every direction: it follows
/// at most three redirects and checks each target like the first; it only
/// uses the standard ports; it reads only HTML, and only up to the end of the
/// document head; and it gives up after a fixed time. Results, including
/// failures, are cached, and everyone who posts the same link shares one
/// fetch.
/// </summary>
public sealed class SitePreviewFetcher
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 400;
    public const int MaxSiteNameLength = 80;
    public const int MaxImageAltLength = 200;
    private const int MaxTypeLength = 40;
    private const int MaxUrlLength = 2048;

    private const int MaxHeadBytes = 256 * 1024;
    private const int MaxRedirects = 3;
    private const int MaxIconCandidates = 2;

    // Smaller images are usually tracking pixels or spacers.
    private const int MinImageSize = 32;
    private const int MinIconSize = 16;
    private const int MaxImageSize = 16384;

    private static readonly TimeSpan FetchTimeLimit = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(5);

    private const string UserAgent = "Mozilla/5.0 (compatible; ValourBot/1.0; +https://valour.gg)";

    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 4000 });
    private static readonly ConcurrentDictionary<string, Lazy<Task<SitePreview>>> InFlight = new();

    // Bounds how many pages the server reads at once, whoever asks.
    private static readonly SemaphoreSlim FetchSlots = new(8);

    private static readonly Regex MetaTag = new(@"<meta\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    private static readonly Regex LinkTag = new(@"<link\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    private static readonly Regex TitleTag = new(@"<title\b[^>]*>([^<]*)<",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    private static readonly Regex TagAttribute = new(
        @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    private static readonly Regex MetaCharset = new(@"<meta\b[^>]*charset\s*=\s*[""']?([A-Za-z0-9_:.-]{1,40})",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

    static SitePreviewFetcher()
    {
        // Pages in legacy encodings such as Shift_JIS or windows-1251.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public SitePreviewFetcher(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Returns the preview for a link, or null when the page has none or
    /// cannot be read safely. The link should have no fragment.
    /// </summary>
    public Task<SitePreview> GetAsync(string url)
    {
        if (url is null || url.Length > MaxUrlLength)
            return Task.FromResult<SitePreview>(null);

        if (Cache.TryGetValue(url, out CachedPreview cached))
            return Task.FromResult(cached.Preview);

        return InFlight.GetOrAdd(url, key => new Lazy<Task<SitePreview>>(() => FetchAndCacheAsync(key))).Value;
    }

    private sealed record CachedPreview(SitePreview Preview);

    private async Task<SitePreview> FetchAndCacheAsync(string url)
    {
        try
        {
            SitePreview preview = null;
            try
            {
                preview = await FetchAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read a link preview for {Url}", url);
            }

            Cache.Set(url, new CachedPreview(preview), new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = preview is null ? FailureLifetime : SuccessLifetime,
            });

            return preview;
        }
        finally
        {
            InFlight.TryRemove(url, out _);
        }
    }

    private async Task<SitePreview> FetchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        using var deadline = new CancellationTokenSource(FetchTimeLimit);
        var cancellationToken = deadline.Token;

        await FetchSlots.WaitAsync(cancellationToken);
        try
        {
            var page = await FetchHeadAsync(uri, cancellationToken);
            if (page is null)
                return null;

            var (pageUri, head) = page.Value;
            var tags = ReadTags(head);

            var title = Clean(tags.First("og:title", "twitter:title") ?? tags.Title, MaxTitleLength);
            var description = Clean(tags.First("og:description", "twitter:description", "description"), MaxDescriptionLength);
            if (title is null && description is null)
                return null;

            var image = await ReadImageAsync(pageUri,
                tags.First("og:image:secure_url", "og:image:url", "og:image", "twitter:image", "twitter:image:src"),
                MinImageSize, cancellationToken);

            SitePreviewImage icon = null;
            foreach (var candidate in tags.IconCandidates.Take(MaxIconCandidates))
            {
                icon = await ReadImageAsync(pageUri, candidate, MinIconSize, cancellationToken);
                if (icon is not null)
                    break;
            }

            return new SitePreview(
                Title: title,
                Description: description,
                SiteName: Clean(tags.First("og:site_name", "application-name"), MaxSiteNameLength),
                Type: Clean(tags.First("og:type"), MaxTypeLength),
                Card: Clean(tags.First("twitter:card"), MaxTypeLength),
                Image: image,
                ImageAlt: image is null ? null : Clean(tags.First("og:image:alt", "twitter:image:alt"), MaxImageAltLength),
                Icon: icon);
        }
        finally
        {
            FetchSlots.Release();
        }
    }

    /// <summary>
    /// Only public web addresses on the standard ports are fetched, and never
    /// this deployment's own hosts.
    /// </summary>
    private async Task<bool> IsAllowedTargetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (ValourHosts.IsSelfHost(uri.Host))
            return false;

        return await OutboundUrlSafetyValidator.IsSafeAsync(uri, _logger, cancellationToken);
    }

    /// <summary>
    /// Fetches the page, following redirects, and returns the address it ended
    /// at with the text of its head.
    /// </summary>
    private async Task<(Uri PageUri, string Head)?> FetchHeadAsync(Uri uri, CancellationToken cancellationToken)
    {
        var current = WithoutFragment(uri);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!await IsAllowedTargetAsync(current, cancellationToken))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
            request.Headers.AcceptLanguage.ParseAdd("en;q=0.8,*;q=0.5");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null)
                    return null;

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);

                // A redirect may not move a secure link onto plain http.
                if (current.Scheme == Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttps)
                    return null;

                current = WithoutFragment(next);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType;
            if (contentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                return null;

            var bytes = await ReadPrefixAsync(response.Content, MaxHeadBytes, cancellationToken);
            var text = Decode(bytes, contentType.CharSet);

            var headEnd = text.IndexOf("</head", StringComparison.OrdinalIgnoreCase);
            return (current, headEnd >= 0 ? text[..headEnd] : text);
        }

        return null;
    }

    private static async Task<byte[]> ReadPrefixAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var total = 0;
        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
                break;

            total += read;
        }

        return buffer.AsSpan(0, total).ToArray();
    }

    /// <summary>
    /// Decodes the page using, in order, its byte order mark, the charset in
    /// its Content-Type, and the charset its markup declares, falling back to
    /// UTF-8.
    /// </summary>
    internal static string Decode(byte[] bytes, string headerCharset)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        var encoding = GetEncoding(headerCharset);
        if (encoding is null)
        {
            var prefix = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
            var declared = MetaCharset.Match(prefix);
            encoding = declared.Success ? GetEncoding(declared.Groups[1].Value) : null;

            // A page can't be UTF-16 and also declare so in ASCII-compatible markup.
            if (encoding is UnicodeEncoding)
                encoding = null;
        }

        return (encoding ?? Encoding.UTF8).GetString(bytes);
    }

    private static Encoding GetEncoding(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            return Encoding.GetEncoding(name.Trim().Trim('"', '\''));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal sealed class PageTags
    {
        public readonly Dictionary<string, string> Meta = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> IconCandidates = new();
        public string Title;

        public string First(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (Meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }
    }

    internal static PageTags ReadTags(string head)
    {
        var tags = new PageTags();

        foreach (Match meta in MetaTag.Matches(head))
        {
            var attributes = ReadAttributes(meta.Value);
            var key = attributes.GetValueOrDefault("property") ??
                      attributes.GetValueOrDefault("name") ??
                      attributes.GetValueOrDefault("itemprop");
            if (key is null || !attributes.TryGetValue("content", out var content))
                continue;

            // The first value wins, as it does for Open Graph readers generally.
            tags.Meta.TryAdd(key.Trim(), content);
        }

        var title = TitleTag.Match(head);
        if (title.Success)
            tags.Title = title.Groups[1].Value;

        // Touch icons are larger and more often a readable format than
        // favicons, so they are tried first.
        var touchIcons = new List<string>();
        var icons = new List<string>();
        foreach (Match link in LinkTag.Matches(head))
        {
            var attributes = ReadAttributes(link.Value);
            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href))
                continue;

            var rel = attributes.GetValueOrDefault("rel")?.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rel is null)
                continue;

            if (rel.Contains("apple-touch-icon") || rel.Contains("apple-touch-icon-precomposed"))
                touchIcons.Add(href);
            else if (rel.Contains("icon"))
                icons.Add(href);
        }

        tags.IconCandidates.AddRange(touchIcons);
        tags.IconCandidates.AddRange(icons);
        return tags;
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in TagAttribute.Matches(tag))
        {
            var value = attribute.Groups[2].Success ? attribute.Groups[2].Value
                : attribute.Groups[3].Success ? attribute.Groups[3].Value
                : attribute.Groups[4].Value;
            attributes.TryAdd(attribute.Groups[1].Value, value);
        }

        return attributes;
    }

    /// <summary>
    /// Checks that an image the page names is a real image of a usable size,
    /// in a format every client can show.
    /// </summary>
    private async Task<SitePreviewImage> ReadImageAsync(Uri pageUri, string candidate, int minSize, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxUrlLength)
            return null;

        if (!Uri.TryCreate(pageUri, WebUtility.HtmlDecode(candidate.Trim()), out var imageUri))
            return null;

        imageUri = WithoutFragment(imageUri);
        if (imageUri.AbsoluteUri.Length > MaxUrlLength ||
            (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps) ||
            !imageUri.IsDefaultPort ||
            !string.IsNullOrEmpty(imageUri.UserInfo))
            return null;

        var info = await ImageSizeFetcher.GetImageDimensionsAsync(
            imageUri.AbsoluteUri, _http, _logger, cancellationToken: cancellationToken);
        if (info is not { } image)
            return null;

        if (image.width < minSize || image.height < minSize ||
            image.width > MaxImageSize || image.height > MaxImageSize)
            return null;

        var (extension, mimeType) = image.format?.ToUpperInvariant() switch
        {
            "PNG" => (".png", "image/png"),
            "JPEG" => (".jpg", "image/jpeg"),
            "WEBP" => (".webp", "image/webp"),
            "GIF" => (".gif", "image/gif"),
            _ => (null, null),
        };

        return extension is null
            ? null
            : new SitePreviewImage(imageUri.AbsoluteUri, image.width, image.height, extension, mimeType);
    }

    private static Uri WithoutFragment(Uri uri) =>
        string.IsNullOrEmpty(uri.Fragment) ? uri : new UriBuilder(uri) { Fragment = string.Empty }.Uri;

    /// <summary>
    /// Decodes entities, removes control and invisible formatting characters
    /// (such as direction overrides, which can disguise text), collapses
    /// whitespace, and shortens to a whole number of characters as people see
    /// them.
    /// </summary>
    public static string Clean(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var decoded = WebUtility.HtmlDecode(value);
        var builder = new StringBuilder(Math.Min(decoded.Length, maxLength * 16));
        var pendingSpace = false;
        var cut = false;

        foreach (var rune in decoded.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // Joiners and emoji tag characters are formatting characters that
            // emoji and some scripts need; other formatting characters go.
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format &&
                rune.Value is not (0x200C or 0x200D) and not (>= 0xE0020 and <= 0xE007F))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());

            // A visible character can span many code units (an emoji family
            // is eight), so this budget only guards against huge input.
            if (builder.Length > maxLength * 16)
            {
                cut = true;
                break;
            }
        }

        var text = builder.ToString();
        var info = new StringInfo(text);
        var elements = info.LengthInTextElements;

        // When the input was cut, its last character may be partial.
        if (cut)
            elements--;

        if (cut || elements > maxLength)
            text = info.SubstringByTextElements(0, Math.Min(elements, maxLength - 1)).TrimEnd() + "\u2026";

        return text.Length == 0 ? null : text;
    }
}
