using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Valour.Database;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
using Valour.Shared.Utilities;
using Valour.Server.Utilities;
using MessageAttachment = Valour.Sdk.Models.MessageAttachment;

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

    // Each preview can take a remote fetch with a long timeout, and message
    // sending waits for all of them, so only the first links get previews
    private const int MaxUrlPreviewsPerMessage = 10;

    public async Task<List<MessageAttachment>> GetUrlAttachmentsFromContent(string url, ValourDb db)
    {
        var urls = CdnUtils.UrlRegex.Matches(url);

        List<MessageAttachment> attachments = null;
        var attempted = 0;

        foreach (Match match in urls)
        {
            // <url> is standard markdown for "link this, but don't embed it" -
            // it still renders as a normal clickable link, it just shouldn't
            // also generate a preview card here.
            if (IsBracketed(url, match))
                continue;

            if (++attempted > MaxUrlPreviewsPerMessage)
                break;

            var attachment = await GetAttachmentFromUrl(match.Value, db);
            if (attachment != null)
            {
                attachment.Inline = true;
                attachment.IsSpoiler = IsInsideSpoiler(url, match.Index);

                if (attachments is null)
                    attachments = new();

                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    private static bool IsBracketed(string url, Match match)
    {
        var start = match.Index - 1;
        var end = match.Index + match.Length;

        if (start < 0 || end >= url.Length)
            return false;

        return url[start] == '<' && url[end] == '>';
    }

    /// <summary>
    /// True if the given index falls inside a ||spoiler|| span, so an embed
    /// generated from a URL there can inherit the spoiler too. Just counts
    /// '||' pairs up to that point rather than running a full Markdig parse.
    /// </summary>
    private static bool IsInsideSpoiler(string url, int index)
    {
        var openSpans = 0;
        var i = 0;
        while (i < index)
        {
            if (url[i] == '|' && i + 1 < url.Length && url[i + 1] == '|')
            {
                openSpans++;
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return openSpans % 2 == 1;
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

        var normalizedHost = NormalizeHost(uri.Host);
        var canonicalUrl = uri.AbsoluteUri;

        // Self-host links become inline previews and are never fetched, so
        // they skip outbound safety validation — which does DNS resolution
        // and would reject configured hosts that don't resolve yet (dev,
        // self-host, or pre-DNS deployments).
        if (ValourHosts.IsSelfHost(normalizedHost))
        {
            return await HandleValourLinkAsync(
                canonicalUrl, new MessageAttachment(MessageAttachmentType.ValourThread), db);
        }

        if (!await OutboundUrlSafetyValidator.IsSafeAsync(uri, _logger))
            return null;

        // Determine if this is a 'virtual' attachment. Like YouTube!
        // these attachments do not have an actual file - usually they use an iframe.
        var isVirtual = CdnUtils.TryGetVirtualAttachmentType(normalizedHost, out var virtualType);
        if (isVirtual)
        {
            return await HandleVirtualAttachment(canonicalUrl, uri, virtualType, db);
        }
        else
        {
            return await HandleMediaAttachment(canonicalUrl, uri, db);
        }
    }

    /// <summary>
    /// Handles virtual attachments (embeds from YouTube, Twitter, etc.)
    /// </summary>
    private async Task<MessageAttachment> HandleVirtualAttachment(string url, Uri uri, MessageAttachmentType virtualType, ValourDb db)
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
                return await HandleTwitter(uri, attachment);

            case MessageAttachmentType.Reddit:
                return await HandleReddit(url, attachment);

            case MessageAttachmentType.TikTok:
                return await HandleTikTok(url, uri, attachment);

            case MessageAttachmentType.Instagram:
                return await HandleInstagram(url, attachment);

            case MessageAttachmentType.Spotify:
                return HandleSpotify(url, uri, attachment);

            case MessageAttachmentType.SoundCloud:
                return await HandleSoundCloud(url, attachment);

            case MessageAttachmentType.GitHub:
                return await HandleGitHub(url, uri, attachment);

            case MessageAttachmentType.Bluesky:
                return await HandleBluesky(url, attachment);

            case MessageAttachmentType.Threads:
                return HandleThreads(uri, attachment);

            case MessageAttachmentType.Streamable:
                return await HandleStreamable(uri, attachment);

            case MessageAttachmentType.AppleMusic:
                return HandleAppleMusic(uri, attachment);

            case MessageAttachmentType.Kick:
                return HandleKick(uri, attachment);

            case MessageAttachmentType.ValourThread:
                return await HandleValourLinkAsync(url, attachment, db);

            default:
                return null;
        }
    }

    #region Valour Links

    /// <summary>
    /// Every self-host URL arrives typed as ValourThread (see
    /// CdnUtils.TryGetVirtualAttachmentType); the actual route decides which
    /// inline preview it becomes. Thread and doc-page links produce cards;
    /// other Valour links fall through to plain links.
    /// </summary>
    private async Task<MessageAttachment> HandleValourLinkAsync(string url, MessageAttachment attachment, ValourDb db)
    {
        if (!ValourRouteParser.TryParse(url, out var route))
            return null;

        switch (route.Type)
        {
            case ValourRouteType.PlanetThread when route.PlanetId is not null && route.ThreadId is not null:
                // Store a canonical in-app link the client parses back into ids.
                attachment.Location = $"{ValourHosts.AppBaseUrl}/planetthreads/{route.PlanetId}/{route.ThreadId}";
                return attachment;

            case ValourRouteType.PlanetWikiPage:
                return await HandleValourWikiPageAsync(route, db);

            default:
                return null;
        }
    }

    /// <summary>
    /// Public docs URLs identify planets by vanity and pages by slug, so
    /// resolve both to ids and store the canonical in-app link.
    /// </summary>
    private async Task<MessageAttachment> HandleValourWikiPageAsync(ValourRoute route, ValourDb db)
    {
        var planetId = route.PlanetId;
        if (planetId is null && route.Vanity is not null)
        {
            planetId = await db.Planets.AsNoTracking()
                .Where(x => x.Vanity == route.Vanity)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
        }

        if (planetId is null)
            return null;

        var pageId = route.PageId;
        if (pageId is null && route.PageSlug is not null)
        {
            pageId = await db.PlanetWikiPages.AsNoTracking()
                .Where(x => x.PlanetId == planetId &&
                            (x.Slug == route.PageSlug || x.PreviousSlug == route.PageSlug))
                .OrderBy(x => x.Slug == route.PageSlug ? 0 : 1)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
        }

        if (pageId is null)
            return null;

        return new MessageAttachment(MessageAttachmentType.ValourWikiPage)
        {
            Location = $"{ValourHosts.AppBaseUrl}/planetwiki/{planetId}/{pageId}",
        };
    }

    #endregion

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

        // Shorts are vertical; the player takes its shape from these.
        if (uri.AbsolutePath.StartsWith("/shorts/"))
        {
            attachment.Width = 9;
            attachment.Height = 16;
        }

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

        // clips.twitch.tv/<slug>
        if (uri.Host.Equals("clips.twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            var slug = path.Trim('/').Split('/')[0];
            if (string.IsNullOrEmpty(slug))
                return null;
            attachment.Location = $"https://clips.twitch.tv/embed?clip={Uri.EscapeDataString(slug)}&parent={ValourHosts.RootDomain}";
            return attachment;
        }

        // Clip, including twitch.tv/<channel>/clip/<slug>
        var channelClip = Regex.Match(path, @"^/[^/]+/clip/([^/]+)");
        if (channelClip.Success)
        {
            attachment.Location = $"https://clips.twitch.tv/embed?clip={Uri.EscapeDataString(channelClip.Groups[1].Value)}&parent={ValourHosts.RootDomain}";
        }
        else if (path.StartsWith("/clip/"))
        {
            var clipId = path.Replace("/clip/", "").TrimEnd('/');
            attachment.Location = $"https://clips.twitch.tv/embed?clip={clipId}&parent={ValourHosts.RootDomain}";
        }
        // Video
        else if (path.StartsWith("/videos/"))
        {
            var videoId = path.Replace("/videos/", "").TrimEnd('/');
            attachment.Location = $"https://player.twitch.tv/?video={videoId}&parent={ValourHosts.RootDomain}";
        }
        // Collection
        else if (path.StartsWith("/collections/"))
        {
            var collectionId = path.Replace("/collections/", "").TrimEnd('/');
            attachment.Location = $"https://player.twitch.tv/?collection={collectionId}&parent={ValourHosts.RootDomain}";
        }
        // Channel (live stream)
        else
        {
            var channel = path.TrimStart('/').Split('/')[0];
            if (string.IsNullOrEmpty(channel))
                return null;
            attachment.Location = $"https://player.twitch.tv/?channel={channel}&parent={ValourHosts.RootDomain}";
        }

        return attachment;
    }

    #endregion

    #region Twitter/X

    // Post links from X, Twitter, and the fxtwitter family all carry the
    // author's handle and the post ID in the same place.
    private static readonly Regex TwitterStatusPath = new(
        @"^/([A-Za-z0-9_]{1,15})/status(?:es)?/(\d{1,20})(?:/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<MessageAttachment> HandleTwitter(Uri uri, MessageAttachment attachment)
    {
        var match = TwitterStatusPath.Match(uri.AbsolutePath);
        if (!match.Success)
            return null;

        var postUrl = $"https://x.com/{match.Groups[1].Value}/status/{match.Groups[2].Value}";
        attachment.Location = postUrl;

        // Current clients frame the post from its ID. The oEmbed markup is
        // kept for clients that still render it.
        try
        {
            var oembedData = await GetCachedOEmbed(
                $"https://publish.x.com/oembed?url={HttpUtility.UrlEncode(postUrl)}&theme=dark&dnt=true&omit_script=true&maxwidth=400&maxheight=400&limit=1&hide_thread=true",
                postUrl);
            if (oembedData is not null)
                attachment.Data = OEmbedSanitizer.Sanitize(oembedData.Html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Twitter oEmbed data for {Url}", postUrl);
        }

        return attachment;
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
            attachment.Data = OEmbedSanitizer.Sanitize(oembedData.Html);
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

    private static readonly Regex TikTokVideoPath = new(
        @"/video/(\d{5,25})(?:/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TikTokVideoIdAttribute = new(
        @"data-video-id=""(\d{5,25})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<MessageAttachment> HandleTikTok(string url, Uri uri, MessageAttachment attachment)
    {
        try
        {
            // Full links carry the video ID. Short links (vm.tiktok.com) do
            // not, but TikTok's oEmbed resolves them and names the video.
            var videoId = TikTokVideoPath.Match(uri.AbsolutePath) is { Success: true } pathMatch
                ? pathMatch.Groups[1].Value
                : null;

            var oembedData = await GetCachedOEmbed(
                $"https://www.tiktok.com/oembed?url={HttpUtility.UrlEncode(url)}",
                url);

            videoId ??= oembedData is null ? null : TikTokVideoIdAttribute.Match(oembedData.Html ?? string.Empty) is { Success: true } htmlMatch
                ? htmlMatch.Groups[1].Value
                : null;

            if (videoId is null)
                return null;

            // TikTok's player page plays in a frame without loading TikTok's
            // script into the app. The oEmbed markup is kept for clients that
            // still render it.
            attachment.Location = $"https://www.tiktok.com/player/v1/{videoId}?description=1&music_info=1&rel=0";
            attachment.Data = oembedData is null ? null : OEmbedSanitizer.Sanitize(oembedData.Html);
            attachment.Width = 325;
            attachment.Height = 578;
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

            // Posts are /p/<code>, reels /reel/<code> or /reels/<code>, and
            // older videos /tv/<code>. The embed page takes /p/ or /reel/.
            if (path.StartsWith("/reels/"))
                path = "/reel/" + path["/reels/".Length..];
            else if (path.StartsWith("/tv/"))
                path = "/p/" + path["/tv/".Length..];

            if (Regex.IsMatch(path, @"^/(?:p|reel)/[A-Za-z0-9_-]+$"))
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
            attachment.Data = OEmbedSanitizer.Sanitize(oembedData.Html);
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

    private static readonly Regex GistPath = new(
        @"^/(?:[A-Za-z0-9-]{1,39}/)?([0-9a-f]{20,40})/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int GistPreviewFiles = 2;
    private const int GistPreviewLines = 15;
    private const int GistPreviewLineLength = 160;

    private static readonly ConcurrentDictionary<string, (string Data, DateTime CachedAt)> _gistCache = new();
    private static readonly TimeSpan GistCacheExpiration = TimeSpan.FromDays(1);

    /// <summary>
    /// Gist links become a card with the first lines of the gist's files,
    /// read from GitHub's API. Other GitHub links have no embed.
    /// </summary>
    private async Task<MessageAttachment> HandleGitHub(string url, Uri uri, MessageAttachment attachment)
    {
        if (!uri.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var match = GistPath.Match(uri.AbsolutePath);
        if (!match.Success)
            return null;

        var gistId = match.Groups[1].Value;
        var data = await GetGistPreviewAsync(gistId);
        if (data is null)
            return null;

        attachment.Location = $"https://gist.github.com/{gistId}";
        attachment.Data = data;
        return attachment;
    }

    private async Task<string> GetGistPreviewAsync(string gistId)
    {
        if (_gistCache.TryGetValue(gistId, out var cached) && DateTime.UtcNow - cached.CachedAt < GistCacheExpiration)
            return cached.Data;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/gists/{gistId}");
            request.Headers.Add("User-Agent", "ValourBot/1.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await CdnLimits.ReadBoundedStringAsync(response.Content, CdnLimits.MaxHtmlScrapeBytes);
            if (json is null)
                return null;

            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;

            var files = new List<GistPreviewFile>();
            if (root.TryGetProperty("files", out var fileMap))
            {
                foreach (var file in fileMap.EnumerateObject())
                {
                    if (files.Count >= GistPreviewFiles)
                        break;

                    var content = file.Value.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    var lines = content.Replace("\r\n", "\n").Split('\n');
                    files.Add(new GistPreviewFile
                    {
                        Name = file.Name,
                        Language = file.Value.TryGetProperty("language", out var l) ? l.GetString() : null,
                        Lines = lines.Take(GistPreviewLines)
                            .Select(x => x.Length > GistPreviewLineLength ? x[..GistPreviewLineLength] : x)
                            .ToList(),
                        TotalLines = lines.Length,
                    });
                }
            }

            var preview = new GistPreview
            {
                Owner = root.TryGetProperty("owner", out var owner) && owner.TryGetProperty("login", out var login)
                    ? login.GetString()
                    : null,
                Description = root.TryGetProperty("description", out var description) ? description.GetString() : null,
                FileCount = root.TryGetProperty("files", out var all) ? all.EnumerateObject().Count() : 0,
                Files = files,
            };

            var data = System.Text.Json.JsonSerializer.Serialize(preview);
            _gistCache[gistId] = (data, DateTime.UtcNow);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch gist {GistId}", gistId);
            return null;
        }
    }

    #endregion

    #region Threads

    private static readonly Regex ThreadsPostPath = new(
        @"^/@([A-Za-z0-9._]{1,30})/post/([A-Za-z0-9_-]{5,20})(?:/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static MessageAttachment HandleThreads(Uri uri, MessageAttachment attachment)
    {
        var match = ThreadsPostPath.Match(uri.AbsolutePath);
        if (!match.Success)
            return null;

        attachment.Location = $"https://www.threads.com/@{match.Groups[1].Value}/post/{match.Groups[2].Value}/embed";
        return attachment;
    }

    #endregion

    #region Streamable

    private static readonly Regex StreamablePath = new(
        @"^/(?:[eos]/)?([A-Za-z0-9]{4,12})/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<MessageAttachment> HandleStreamable(Uri uri, MessageAttachment attachment)
    {
        var match = StreamablePath.Match(uri.AbsolutePath);
        if (!match.Success)
            return null;

        var videoId = match.Groups[1].Value;
        attachment.Location = $"https://streamable.com/e/{videoId}";

        // The player takes its shape from the video's size; without it,
        // it assumes 16:9.
        try
        {
            var videoUrl = $"https://streamable.com/{videoId}";
            var oembedData = await GetCachedOEmbed(
                $"https://api.streamable.com/oembed.json?url={HttpUtility.UrlEncode(videoUrl)}",
                videoUrl);
            attachment.Width = oembedData?.Width ?? 0;
            attachment.Height = oembedData?.Height ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Streamable oEmbed data for {VideoId}", videoId);
        }

        return attachment;
    }

    #endregion

    #region Apple Music

    private static readonly Regex AppleMusicPath = new(
        @"^/[a-z]{2}/(album|playlist|song)/[^/]+/[A-Za-z0-9.]+/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static MessageAttachment HandleAppleMusic(Uri uri, MessageAttachment attachment)
    {
        var match = AppleMusicPath.Match(uri.AbsolutePath);
        if (!match.Success)
            return null;

        // An album link with ?i=<track> points at one song on the album.
        var trackId = HttpUtility.ParseQueryString(uri.Query)["i"];
        var isSong = match.Groups[1].Value == "song" ||
                     (!string.IsNullOrEmpty(trackId) && trackId.All(char.IsAsciiDigit));

        attachment.Location = $"https://embed.music.apple.com{uri.AbsolutePath}" +
                              (isSong && match.Groups[1].Value == "album" ? $"?i={trackId}" : string.Empty);
        attachment.Width = 660;
        attachment.Height = isSong ? 175 : 450;
        return attachment;
    }

    #endregion

    #region Kick

    private static readonly Regex KickChannelPath = new(
        @"^/([A-Za-z0-9_]{3,25})/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Site pages that share the channel URL shape.
    private static readonly HashSet<string> KickReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "browse", "categories", "category", "following", "search", "settings", "dashboard", "terms-of-service", "privacy-policy",
    };

    private static MessageAttachment HandleKick(Uri uri, MessageAttachment attachment)
    {
        var match = KickChannelPath.Match(uri.AbsolutePath);
        if (!match.Success || KickReservedPaths.Contains(match.Groups[1].Value))
            return null;

        attachment.Location = $"https://player.kick.com/{match.Groups[1].Value}";
        return attachment;
    }

    #endregion

    #region Bluesky

    private static readonly Regex BlueskyPostUri = new(
        @"data-bluesky-uri=""at://(did:[a-z]+:[A-Za-z0-9._:%-]+)/app\.bsky\.feed\.post/([A-Za-z0-9]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<MessageAttachment> HandleBluesky(string url, MessageAttachment attachment)
    {
        try
        {
            if (!new Uri(url).AbsolutePath.Contains("/post/"))
                return null;

            // The embed page only accepts the post's AT URI, which names the
            // account by DID rather than handle. The oEmbed markup carries it.
            var oembedData = await GetCachedOEmbed(
                $"https://embed.bsky.app/oembed?url={HttpUtility.UrlEncode(url)}",
                url);

            var postUri = BlueskyPostUri.Match(oembedData?.Html ?? string.Empty);
            if (!postUri.Success)
                return null;

            attachment.Location = $"https://embed.bsky.app/embed/{postUri.Groups[1].Value}/app.bsky.feed.post/{postUri.Groups[2].Value}";
            attachment.Width = 400;
            attachment.Height = 300;
            return attachment;
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

            // Bound the read: the origin is user-chosen and may serve an
            // enormous or slow body. OpenGraph tags are in the head anyway.
            var html = await CdnLimits.ReadBoundedStringAsync(response.Content, CdnLimits.MaxHtmlScrapeBytes);
            if (html is null)
                return null;

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
        if (CdnUtils.IsMediaBypassHost(normalizedHost))
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

                attachment.Location = $"{ValourHosts.ContentCdnBaseUrl}/proxy/{hash}{ext}";
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
