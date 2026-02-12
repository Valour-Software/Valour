using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Valour.Database;
using Valour.Sdk.Models;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
using Valour.Server.Utilities;

namespace Valour.Server.Cdn;

public class ProxyHandler
{
    private readonly HttpClient _http;
    private readonly ILogger<ProxyHandler> _logger;
    private static int _cleanupStarted;

    // Cache for oEmbed responses (15 minute expiration)
    private static readonly ConcurrentDictionary<string, CachedOEmbed> _oembedCache = new();
    private static readonly TimeSpan OEmbedCacheExpiration = TimeSpan.FromMinutes(15);

    // Cache for Open Graph data (15 minute expiration)
    private static readonly ConcurrentDictionary<string, CachedOpenGraph> _openGraphCache = new();
    private static readonly TimeSpan OpenGraphCacheExpiration = TimeSpan.FromMinutes(15);

    private class CachedOEmbed
    {
        public OembedData Data { get; set; }
        public DateTime CachedAt { get; set; }
        public bool IsExpired => DateTime.UtcNow - CachedAt > OEmbedCacheExpiration;
    }

    private class CachedOpenGraph
    {
        public OpenGraphData Data { get; set; }
        public DateTime CachedAt { get; set; }
        public bool IsExpired => DateTime.UtcNow - CachedAt > OpenGraphCacheExpiration;
    }

    public ProxyHandler(HttpClient http, ILogger<ProxyHandler> logger)
    {
        _http = http;
        _logger = logger;

        // Start one cleanup loop for all ProxyHandler instances.
        if (Interlocked.Exchange(ref _cleanupStarted, 1) == 0)
            _ = Task.Run(CleanupCacheAsync);
    }

    private static async Task CleanupCacheAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));

            var expiredOEmbed = _oembedCache.Where(x => x.Value.IsExpired).Select(x => x.Key).ToList();
            foreach (var key in expiredOEmbed)
                _oembedCache.TryRemove(key, out _);

            var expiredOg = _openGraphCache.Where(x => x.Value.IsExpired).Select(x => x.Key).ToList();
            foreach (var key in expiredOg)
                _openGraphCache.TryRemove(key, out _);
        }
    }

    public async Task<List<MessageAttachment>> GetUrlAttachmentsFromContent(string url, ValourDb db)
    {
        var urls = CdnUtils.UrlRegex.Matches(url);

        List<MessageAttachment> attachments = null;

        foreach (Match match in urls)
        {
            var attachment = await GetAttachmentFromUrl(match.Value, db);
            if (attachment != null)
            {
                if (attachments is null)
                    attachments = new();

                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    /// <summary>
    /// Given a url, returns the attachment object
    /// This can be an image, video, or even an embed website view
    /// Also handles converting to ValourCDN when necessary
    /// </summary>
    public async Task<MessageAttachment> GetAttachmentFromUrl(string url, ValourDb db)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (!await OutboundUrlSafetyValidator.IsSafeAsync(uri, _logger))
            return null;

        var normalizedHost = NormalizeHost(uri.Host);
        var canonicalUrl = uri.AbsoluteUri;

        // Determine if this is a 'virtual' attachment. Like YouTube!
        // these attachments do not have an actual file - usually they use an iframe.
        var isVirtual = CdnUtils.VirtualAttachmentMap.TryGetValue(normalizedHost, out var virtualType);
        if (isVirtual)
        {
            return await HandleVirtualAttachment(canonicalUrl, uri, virtualType);
        }
        else
        {
            return await HandleMediaAttachment(canonicalUrl, uri, db);
        }
    }

    /// <summary>
    /// Handles virtual attachments (embeds from YouTube, Twitter, etc.)
    /// </summary>
    private async Task<MessageAttachment> HandleVirtualAttachment(string url, Uri uri, MessageAttachmentType virtualType)
    {
        var attachment = new MessageAttachment(virtualType);

        switch (virtualType)
        {
            case MessageAttachmentType.YouTube:
                return HandleYouTube(url, uri, attachment);

            case MessageAttachmentType.Vimeo:
                return HandleVimeo(uri, attachment);

            case MessageAttachmentType.Twitch:
                return HandleTwitch(uri, attachment);

            case MessageAttachmentType.Twitter:
                return await HandleTwitter(url, attachment);

            case MessageAttachmentType.Reddit:
                return await HandleReddit(url, attachment);

            case MessageAttachmentType.TikTok:
                return await HandleTikTok(url, attachment);

            case MessageAttachmentType.Instagram:
                return await HandleInstagram(url, attachment);

            case MessageAttachmentType.Spotify:
                return HandleSpotify(url, uri, attachment);

            case MessageAttachmentType.SoundCloud:
                return await HandleSoundCloud(url, attachment);

            case MessageAttachmentType.GitHub:
                return HandleGitHub(url, uri, attachment);

            case MessageAttachmentType.Bluesky:
                return await HandleBluesky(url, attachment);

            default:
                return null;
        }
    }

    #region YouTube

    private MessageAttachment HandleYouTube(string url, Uri uri, MessageAttachment attachment)
    {
        string videoId = null;
        string timestamp = null;

        // Parse timestamp if present
        var query = HttpUtility.ParseQueryString(uri.Query);
        timestamp = query["t"] ?? query["start"];

        // YouTube Shorts
        if (uri.AbsolutePath.StartsWith("/shorts/"))
        {
            videoId = uri.Segments.LastOrDefault()?.TrimEnd('/');
        }
        // Standard watch URL (?v=)
        else if (query["v"] != null)
        {
            videoId = query["v"];
        }
        // Embed URL (/embed/)
        else if (uri.AbsolutePath.StartsWith("/embed/"))
        {
            videoId = uri.Segments.LastOrDefault()?.TrimEnd('/');
        }
        // Short URL (youtu.be)
        else if (uri.Host == "youtu.be")
        {
            videoId = uri.AbsolutePath.TrimStart('/').Split('?')[0];
        }
        // YouTube Music
        else if (uri.Host == "music.youtube.com" && query["v"] != null)
        {
            videoId = query["v"];
        }
        // Playlist - embed the playlist
        else if (query["list"] != null && videoId == null)
        {
            attachment.Location = $"https://www.youtube.com/embed/videoseries?list={query["list"]}";
            return attachment;
        }

        if (string.IsNullOrEmpty(videoId))
            return null;

        var embedUrl = $"https://www.youtube.com/embed/{videoId}";

        // Add timestamp if present
        if (!string.IsNullOrEmpty(timestamp))
        {
            // Convert timestamp to seconds if needed (e.g., "1m30s" -> "90")
            var seconds = ParseYouTubeTimestamp(timestamp);
            if (seconds > 0)
                embedUrl += $"?start={seconds}";
        }

        // If there's also a playlist, include it
        if (query["list"] != null)
        {
            embedUrl += (embedUrl.Contains("?") ? "&" : "?") + $"list={query["list"]}";
        }

        attachment.Location = embedUrl;
        return attachment;
    }

    private static int ParseYouTubeTimestamp(string timestamp)
    {
        if (int.TryParse(timestamp, out var seconds))
            return seconds;

        // Parse formats like "1m30s", "1h2m3s", etc.
        var match = Regex.Match(timestamp, @"(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?");
        if (match.Success)
        {
            var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
            var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            var secs = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            return hours * 3600 + minutes * 60 + secs;
        }

        return 0;
    }

    #endregion

    #region Vimeo

    private MessageAttachment HandleVimeo(Uri uri, MessageAttachment attachment)
    {
        var videoId = uri.Segments.LastOrDefault()?.TrimEnd('/');
        if (string.IsNullOrEmpty(videoId))
            return null;

        attachment.Location = $"https://player.vimeo.com/video/{videoId}";
        return attachment;
    }

    #endregion

    #region Twitch

    private MessageAttachment HandleTwitch(Uri uri, MessageAttachment attachment)
    {
        var path = uri.AbsolutePath;

        // Clip
        if (path.StartsWith("/clip/"))
        {
            var clipId = path.Replace("/clip/", "").TrimEnd('/');
            attachment.Location = $"https://clips.twitch.tv/embed?clip={clipId}&parent=valour.gg";
        }
        // Video
        else if (path.StartsWith("/videos/"))
        {
            var videoId = path.Replace("/videos/", "").TrimEnd('/');
            attachment.Location = $"https://player.twitch.tv/?video={videoId}&parent=valour.gg";
        }
        // Collection
        else if (path.StartsWith("/collections/"))
        {
            var collectionId = path.Replace("/collections/", "").TrimEnd('/');
            attachment.Location = $"https://player.twitch.tv/?collection={collectionId}&parent=valour.gg";
        }
        // Channel (live stream)
        else
        {
            var channel = path.TrimStart('/').Split('/')[0];
            if (string.IsNullOrEmpty(channel))
                return null;
            attachment.Location = $"https://player.twitch.tv/?channel={channel}&parent=valour.gg";
        }

        return attachment;
    }

    #endregion

    #region Twitter/X

    private async Task<MessageAttachment> HandleTwitter(string url, MessageAttachment attachment)
    {
        try
        {
            var oembedData = await GetCachedOEmbed(
                $"https://publish.twitter.com/oembed?url={HttpUtility.UrlEncode(url)}&theme=dark&dnt=true&omit_script=true&maxwidth=400&maxheight=400&limit=1&hide_thread=true",
                url);

            if (oembedData == null)
                return null;

            attachment.Location = url;
            attachment.Html = OEmbedSanitizer.Sanitize(oembedData.Html);
            return attachment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Twitter oEmbed data for {Url}", url);
            return null;
        }
    }

    #endregion

    #region Reddit

    private async Task<MessageAttachment> HandleReddit(string url, MessageAttachment attachment)
    {
        try
        {
            var oembedData = await GetCachedOEmbed(
                $"https://www.reddit.com/oembed?url={HttpUtility.UrlEncode(url)}",
                url);

            if (oembedData == null)
                return null;

            attachment.Location = url;
            attachment.Html = OEmbedSanitizer.Sanitize(oembedData.Html);
            attachment.Height = oembedData.Height ?? 240;
            return attachment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Reddit oEmbed data for {Url}", url);
            return null;
        }
    }

    #endregion

    #region TikTok

    private async Task<MessageAttachment> HandleTikTok(string url, MessageAttachment attachment)
    {
        try
        {
            var oembedData = await GetCachedOEmbed(
                $"https://www.tiktok.com/oembed?url={HttpUtility.UrlEncode(url)}",
                url);

            if (oembedData == null)
                return null;

            attachment.Location = url;
            attachment.Html = OEmbedSanitizer.Sanitize(oembedData.Html);
            attachment.Width = oembedData.Width ?? 325;
            attachment.Height = oembedData.Height ?? 580;
            return attachment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch TikTok oEmbed data for {Url}", url);
            return null;
        }
    }

    #endregion

    #region Instagram

    private async Task<MessageAttachment> HandleInstagram(string url, MessageAttachment attachment)
    {
        try
        {
            // Instagram requires access token for oEmbed API, so we'll use iframe embed
            var uri = new Uri(url);
            var path = uri.AbsolutePath.TrimEnd('/');

            // Extract post/reel ID from URL patterns like /p/ABC123/ or /reel/ABC123/
            if (path.StartsWith("/p/") || path.StartsWith("/reel/"))
            {
                // Use the embed URL format
                attachment.Location = $"https://www.instagram.com{path}/embed/";
                attachment.Width = 400;
                attachment.Height = 480;
                return attachment;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Instagram embed for {Url}", url);
            return null;
        }
    }

    #endregion

    #region Spotify

    private MessageAttachment HandleSpotify(string url, Uri uri, MessageAttachment attachment)
    {
        try
        {
            var path = uri.AbsolutePath;

            // Spotify URLs are like /track/ID, /album/ID, /playlist/ID, /artist/ID, /episode/ID
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
                return null;

            var contentType = segments[0]; // track, album, playlist, artist, episode
            var contentId = segments[1];

            // Validate content type
            var validTypes = new[] { "track", "album", "playlist", "artist", "episode", "show" };
            if (!validTypes.Contains(contentType))
                return null;

            // Create embed URL
            attachment.Location = $"https://open.spotify.com/embed/{contentType}/{contentId}";
            attachment.Width = 300;
            attachment.Height = contentType == "track" ? 80 : 380;
            return attachment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Spotify embed for {Url}", url);
            return null;
        }
    }

    #endregion

    #region SoundCloud

    private async Task<MessageAttachment> HandleSoundCloud(string url, MessageAttachment attachment)
    {
        try
        {
            var oembedData = await GetCachedOEmbed(
                $"https://soundcloud.com/oembed?format=json&url={HttpUtility.UrlEncode(url)}",
                url);

            if (oembedData == null)
                return null;

            attachment.Location = url;
            attachment.Html = OEmbedSanitizer.Sanitize(oembedData.Html);
            attachment.Width = oembedData.Width ?? 100;
            attachment.Height = oembedData.Height ?? 166;
            return attachment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch SoundCloud oEmbed data for {Url}", url);
            return null;
        }
    }

    #endregion

    #region GitHub

    private MessageAttachment HandleGitHub(string url, Uri uri, MessageAttachment attachment)
    {
        try
        {
            // GitHub Gist embed
            if (uri.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase))
            {
                // Keep canonical gist URL and let the client load the known embed script directly.
                attachment.Location = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
                attachment.Html = null;
                return attachment;
            }

            // Regular GitHub URLs - use Open Graph preview instead
            attachment.Type = MessageAttachmentType.SitePreview;
            return null; // Will fall through to Open Graph handling
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create GitHub embed for {Url}", url);
            return null;
        }
    }

    #endregion

    #region Bluesky

    private async Task<MessageAttachment> HandleBluesky(string url, MessageAttachment attachment)
    {
        try
        {
            // Bluesky posts can be embedded via iframe
            var uri = new Uri(url);
            var path = uri.AbsolutePath;

            // URLs are like /profile/user.bsky.social/post/ABC123
            if (path.Contains("/post/"))
            {
                // Convert to embed URL
                var embedUrl = $"https://embed.bsky.app/embed{path}";
                attachment.Location = embedUrl;
                attachment.Width = 400;
                attachment.Height = 300;
                return attachment;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Bluesky embed for {Url}", url);
            return null;
        }
    }

    #endregion

    #region Open Graph / Site Preview

    /// <summary>
    /// Fetches Open Graph metadata for a URL to create a site preview
    /// </summary>
    public async Task<OpenGraphData> GetOpenGraphDataAsync(string url)
    {
        if (_openGraphCache.TryGetValue(url, out var cached) && !cached.IsExpired)
            return cached.Data;

        if (!await OutboundUrlSafetyValidator.IsSafeAsync(url, _logger))
            return null;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; ValourBot/1.0)");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync();
            var ogData = ParseOpenGraphTags(html, url);

            if (ogData != null)
            {
                _openGraphCache[url] = new CachedOpenGraph
                {
                    Data = ogData,
                    CachedAt = DateTime.UtcNow
                };
            }

            return ogData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Open Graph data for {Url}", url);
            return null;
        }
    }

    private static readonly Regex OgTagPattern = new(
        @"<meta\s+(?:property|name)\s*=\s*[""'](?:og:|twitter:)(\w+)[""']\s+content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OgTagPatternReverse = new(
        @"<meta\s+content\s*=\s*[""']([^""']*)[""']\s+(?:property|name)\s*=\s*[""'](?:og:|twitter:)(\w+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitlePattern = new(
        @"<title[^>]*>([^<]+)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DescriptionPattern = new(
        @"<meta\s+name\s*=\s*[""']description[""']\s+content\s*=\s*[""']([^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private OpenGraphData ParseOpenGraphTags(string html, string url)
    {
        var data = new OpenGraphData { Url = url };

        // Parse OG/Twitter meta tags
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in OgTagPattern.Matches(html))
        {
            tags[match.Groups[1].Value] = HttpUtility.HtmlDecode(match.Groups[2].Value);
        }

        foreach (Match match in OgTagPatternReverse.Matches(html))
        {
            if (!tags.ContainsKey(match.Groups[2].Value))
                tags[match.Groups[2].Value] = HttpUtility.HtmlDecode(match.Groups[1].Value);
        }

        // Map to OpenGraphData
        data.Title = tags.GetValueOrDefault("title");
        data.Description = tags.GetValueOrDefault("description");
        data.Image = tags.GetValueOrDefault("image");
        data.SiteName = tags.GetValueOrDefault("site_name");
        data.Type = tags.GetValueOrDefault("type");

        // Fallback to standard HTML tags
        if (string.IsNullOrWhiteSpace(data.Title))
        {
            var titleMatch = TitlePattern.Match(html);
            if (titleMatch.Success)
                data.Title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
        }

        if (string.IsNullOrWhiteSpace(data.Description))
        {
            var descMatch = DescriptionPattern.Match(html);
            if (descMatch.Success)
                data.Description = HttpUtility.HtmlDecode(descMatch.Groups[1].Value);
        }

        // Make image URL absolute if relative
        if (!string.IsNullOrWhiteSpace(data.Image) && !data.Image.StartsWith("http"))
        {
            var baseUri = new Uri(url);
            data.Image = new Uri(baseUri, data.Image).ToString();
        }

        return data.IsValid ? data : null;
    }

    #endregion

    #region oEmbed Caching

    private async Task<OembedData> GetCachedOEmbed(string oembedUrl, string originalUrl)
    {
        if (_oembedCache.TryGetValue(originalUrl, out var cached) && !cached.IsExpired)
            return cached.Data;

        var data = await _http.GetFromJsonAsync<OembedData>(oembedUrl);

        if (data != null)
        {
            _oembedCache[originalUrl] = new CachedOEmbed
            {
                Data = data,
                CachedAt = DateTime.UtcNow
            };
        }

        return data;
    }

    #endregion

    #region Media Attachments

    /// <summary>
    /// Handles regular media attachments (images, videos, audio, files)
    /// </summary>
    private async Task<MessageAttachment> HandleMediaAttachment(string url, Uri uri, ValourDb db)
    {
        if (!await OutboundUrlSafetyValidator.IsSafeAsync(uri, _logger))
            return null;

        // We have to determine the type of the attachment
        var name = Path.GetFileName(uri.AbsoluteUri);
        var ext = Path.GetExtension(name).ToLower();

        // Try to get media type
        CdnUtils.ExtensionToMimeType.TryGetValue(ext, out var mime);

        // Default type is file
        var type = MessageAttachmentType.File;

        // It's not media, so we just treat it as a link
        if (mime is null || mime.Length < 4)
        {
            return null;
        }
        // Is media
        else
        {
            // Determine if audio or video or image

            // We only actually need to check the first letter,
            // since only 'image/' starts with i
            if (mime[0] == 'i')
            {
                type = MessageAttachmentType.Image;
            }
            // Same thing here - only 'video/' starts with v
            else if (mime[0] == 'v')
            {
                type = MessageAttachmentType.Video;
            }
            // Unfortunately 'audio/' and 'application/' both start with 'a'
            else if (mime[0] == 'a' && mime[1] == 'u')
            {
                type = MessageAttachmentType.Audio;
            }
        }

        // Bypass our own CDN for known good sources
        var normalizedHost = NormalizeHost(uri.Host);
        if (CdnUtils.MediaBypassList.Contains(normalizedHost))
        {
            return new MessageAttachment(type)
            {
                Location = url,
                MimeType = mime,
                FileName = name,
            };
        }
        // Use our own CDN
        else
        {
            // Get hash from uri (using thread-safe static method)
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            var attachment = new MessageAttachment(type)
            {
                MimeType = mime,
                FileName = name,
            };

            // Check if we have already proxied this item
            var item = await db.CdnProxyItems.FindAsync(hash + ext);
            if (item is null)
            {
                if (type == MessageAttachmentType.Image)
                {
                    var imageMeta = await ImageSizeFetcher.GetImageDimensionsAsync(uri.AbsoluteUri, _http, _logger);
                    if (imageMeta is not null)
                    {
                        attachment.Width = imageMeta.Value.width;
                        attachment.Height = imageMeta.Value.height;
                    }
                    else
                    {
                        return null;
                    }
                }

                item = new CdnProxyItem()
                {
                    Id = hash + ext,
                    Origin = uri.AbsoluteUri,
                    MimeType = mime,
                    Width = attachment.Width,
                    Height = attachment.Height
                };

                await db.AddAsync(item);
                await db.SaveChangesAsync();

                attachment.Location = $"https://cdn.valour.gg/proxy/{hash}{ext}";
            }
            else
            {
                if (!await OutboundUrlSafetyValidator.IsSafeAsync(item.Origin, _logger))
                    return null;

                attachment.Location = item.Url;
                attachment.Width = item.Width ?? 0;
                attachment.Height = item.Height ?? 0;
            }

            return attachment;
        }
    }

    private static string NormalizeHost(string host)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return host[4..];

        return host;
    }

    #endregion
}
