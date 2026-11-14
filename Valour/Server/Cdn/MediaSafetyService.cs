using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Cdn;

public class MediaSafetyHashMatchResult
{
    public MediaSafetyHashMatchState State { get; set; }
    public string Provider { get; set; }
    public string MatchId { get; set; }
    public string Details { get; set; }
    public DateTime? HashMatchedAt { get; set; }
    public bool ShouldBlock { get; set; }

    public static MediaSafetyHashMatchResult Skipped(string provider, string details = null) => new()
    {
        State = MediaSafetyHashMatchState.Skipped,
        Provider = provider,
        Details = details
    };

    public static MediaSafetyHashMatchResult Error(string provider, string details, bool shouldBlock) => new()
    {
        State = MediaSafetyHashMatchState.Error,
        Provider = provider,
        Details = details,
        HashMatchedAt = DateTime.UtcNow,
        ShouldBlock = shouldBlock
    };
}

/// <summary>
/// Checks uploaded images against known child sexual abuse material with
/// Microsoft PhotoDNA. In Enforce mode a match blocks the upload and is handed
/// to <see cref="MediaSafetyIncidentService"/>.
/// </summary>
public class MediaSafetyService
{
    /// <summary>The image formats PhotoDNA accepts. Others are converted to PNG to be checked.</summary>
    private static readonly HashSet<string> PhotoDnaFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/tiff",
    };

    /// <summary>The PhotoDNA status code for a request that was processed.</summary>
    private const string PhotoDnaStatusOk = "3000";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MediaSafetyIncidentService _incidents;
    private readonly ILogger<MediaSafetyService> _logger;

    public MediaSafetyService(
        IHttpClientFactory httpClientFactory,
        MediaSafetyIncidentService incidents,
        ILogger<MediaSafetyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _incidents = incidents;
        _logger = logger;
    }

    /// <summary>
    /// Checks an image before it is stored. A blocked match has already been
    /// preserved, reported and acted on when this returns, so the caller only
    /// needs to reject the upload.
    /// </summary>
    public async Task<MediaSafetyHashMatchResult> HashMatchImageUploadAsync(
        MemoryStream data,
        string fileName,
        string mimeType,
        long uploaderUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await CheckImageAsync(data, fileName, mimeType, cancellationToken);

        if (result.State == MediaSafetyHashMatchState.Matched && result.ShouldBlock)
        {
            await _incidents.HandleMatchAsync(uploaderUserId, data, fileName, mimeType, result);
        }
        else if (result.State == MediaSafetyHashMatchState.Matched)
        {
            _logger.LogCritical(
                "Upload by user {UserId} matched known abuse material ({MatchId}) but media safety is not in Enforce mode",
                uploaderUserId, result.MatchId);
        }

        data.Position = 0;
        return result;
    }

    private async Task<MediaSafetyHashMatchResult> CheckImageAsync(
        MemoryStream data,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var config = MediaSafetyConfig.Current ?? new MediaSafetyConfig();
        var provider = string.IsNullOrWhiteSpace(config.Provider) ? "PhotoDNA" : config.Provider;

        if (!config.Enabled ||
            IsOff(config.Mode) ||
            !config.HashMatchImageUploads)
        {
            return MediaSafetyHashMatchResult.Skipped(provider, "Media safety hash matching is disabled.");
        }

        if (!provider.Equals("PhotoDNA", StringComparison.OrdinalIgnoreCase))
        {
            return MediaSafetyHashMatchResult.Error(provider, $"Unsupported media safety provider '{provider}'.", config.FailClosed);
        }

        if (string.IsNullOrWhiteSpace(config.PhotoDnaEndpoint) ||
            string.IsNullOrWhiteSpace(config.PhotoDnaSubscriptionKey))
        {
            return MediaSafetyHashMatchResult.Error(provider, "PhotoDNA endpoint or subscription key is not configured.", config.FailClosed);
        }

        try
        {
            var hashMatch = await HashMatchPhotoDnaAsync(data, fileName, mimeType, config, cancellationToken);
            hashMatch.ShouldBlock = hashMatch.State switch
            {
                MediaSafetyHashMatchState.Matched => IsEnforce(config.Mode),
                MediaSafetyHashMatchState.Error => config.FailClosed,
                _ => false,
            };
            return hashMatch;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "PhotoDNA hash match failed for {FileName}", fileName);
            return MediaSafetyHashMatchResult.Error(provider, "PhotoDNA hash match failed.", config.FailClosed);
        }
    }

    public static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task<MediaSafetyHashMatchResult> HashMatchPhotoDnaAsync(
        MemoryStream data,
        string fileName,
        string mimeType,
        MediaSafetyConfig config,
        CancellationToken cancellationToken)
    {
        var timeout = config.TimeoutSeconds <= 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(config.TimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var client = _httpClientFactory.CreateClient("PhotoDNA");
        using var request = new HttpRequestMessage(HttpMethod.Post, config.PhotoDnaEndpoint);
        request.Headers.TryAddWithoutValidation(config.PhotoDnaHeaderName, config.PhotoDnaSubscriptionKey);

        // PhotoDNA's Match operation takes the image bytes as the request body.
        var (bytes, contentType) = await GetScannableImageAsync(data, mimeType, timeoutCts.Token);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = content;

        using var response = await client.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            return MediaSafetyHashMatchResult.Error(
                "PhotoDNA",
                $"PhotoDNA returned {(int)response.StatusCode}: {TrimDetails(body)}",
                config.FailClosed);
        }

        var parsed = ParsePhotoDnaHashMatchResponse(body);
        parsed.Provider = "PhotoDNA";
        parsed.HashMatchedAt = DateTime.UtcNow;
        data.Position = 0;

        return parsed;
    }

    /// <summary>
    /// Returns the image in a format PhotoDNA accepts, converting others to PNG.
    /// </summary>
    private static async Task<(byte[] Bytes, string ContentType)> GetScannableImageAsync(
        MemoryStream data, string mimeType, CancellationToken cancellationToken)
    {
        data.Position = 0;
        if (mimeType is not null && PhotoDnaFormats.Contains(mimeType))
            return (data.ToArray(), mimeType);

        using var image = await Image.LoadAsync(data, cancellationToken);
        using var png = new MemoryStream();
        await image.SaveAsync(png, new PngEncoder(), cancellationToken);
        data.Position = 0;
        return (png.ToArray(), "image/png");
    }

    /// <summary>
    /// Reads a PhotoDNA Match response. A processed request has status code
    /// 3000 and says whether the image matched. Any other status, or a
    /// response that does not say, is an error and never a clean result.
    /// Property names are matched without regard to case.
    /// </summary>
    public static MediaSafetyHashMatchResult ParsePhotoDnaHashMatchResponse(string body)
    {
        JsonNode node = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
                node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
        }

        if (node is not JsonObject root)
        {
            return new MediaSafetyHashMatchResult
            {
                State = MediaSafetyHashMatchState.Error,
                Details = TrimDetails(body) ?? "PhotoDNA returned an empty or unreadable response."
            };
        }

        var statusCode = GetProperty(GetProperty(root, "Status"), "Code")?.ToString();
        var isMatch = GetBool(GetProperty(root, "IsMatch"));

        if ((statusCode is not null && statusCode != PhotoDnaStatusOk) || isMatch is null)
        {
            return new MediaSafetyHashMatchResult
            {
                State = MediaSafetyHashMatchState.Error,
                MatchId = GetProperty(root, "TrackingId")?.ToString(),
                Details = TrimDetails(body)
            };
        }

        return new MediaSafetyHashMatchResult
        {
            State = isMatch.Value ? MediaSafetyHashMatchState.Matched : MediaSafetyHashMatchState.NoMatch,
            MatchId = FindMatchId(root) ?? GetProperty(root, "TrackingId")?.ToString(),
            Details = TrimDetails(body)
        };
    }

    /// <summary>
    /// Finds the database match ID in MatchDetails.MatchFlags[].AdvancedInfo.
    /// </summary>
    private static string FindMatchId(JsonObject root)
    {
        if (GetProperty(GetProperty(root, "MatchDetails"), "MatchFlags") is not JsonArray flags)
            return null;

        foreach (var flag in flags)
        {
            if (GetProperty(flag, "AdvancedInfo") is not JsonArray info)
                continue;

            foreach (var entry in info)
            {
                if (string.Equals(GetProperty(entry, "Key")?.ToString(), "MatchId", StringComparison.OrdinalIgnoreCase))
                    return GetProperty(entry, "Value")?.ToString();
            }
        }

        return null;
    }

    private static JsonNode GetProperty(JsonNode node, string name)
    {
        if (node is not JsonObject obj)
            return null;

        foreach (var (key, value) in obj)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static bool? GetBool(JsonNode value)
    {
        if (value is null)
            return null;

        return value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    private static bool IsOff(string mode)
    {
        return string.IsNullOrWhiteSpace(mode) ||
               mode.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnforce(string mode)
    {
        return mode.Equals("Enforce", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("Block", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return null;

        return details.Length <= 2048 ? details : details[..2048];
    }
}
