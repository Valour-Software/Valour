using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public class MediaSafetyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaSafetyService> _logger;

    public MediaSafetyService(IHttpClientFactory httpClientFactory, ILogger<MediaSafetyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<MediaSafetyHashMatchResult> HashMatchImageUploadAsync(
        MemoryStream data,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
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
            hashMatch.ShouldBlock = hashMatch.State == MediaSafetyHashMatchState.Matched && IsEnforce(config.Mode);
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

        data.Position = 0;
        var content = new ByteArrayContent(data.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = $"\"{fileName}\""
        };

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

    private static MediaSafetyHashMatchResult ParsePhotoDnaHashMatchResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new MediaSafetyHashMatchResult
            {
                State = MediaSafetyHashMatchState.NoMatch,
                Details = "PhotoDNA returned an empty success response."
            };
        }

        JsonNode node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch
        {
            return new MediaSafetyHashMatchResult
            {
                State = MediaSafetyHashMatchState.NoMatch,
                Details = TrimDetails(body)
            };
        }

        var matched =
            TryGetBool(node, "isMatch") ??
            TryGetBool(node, "matched") ??
            TryGetBool(node, "match") ??
            TryGetBool(node, "matchFound") ??
            TryGetString(node, "status")?.Equals("match", StringComparison.OrdinalIgnoreCase) ??
            false;

        return new MediaSafetyHashMatchResult
        {
            State = matched ? MediaSafetyHashMatchState.Matched : MediaSafetyHashMatchState.NoMatch,
            MatchId = TryGetString(node, "matchId") ??
                      TryGetString(node, "id") ??
                      TryGetString(node, "hashId") ??
                      TryGetString(node, "trackingId"),
            Details = TrimDetails(body)
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

    private static bool? TryGetBool(JsonNode node, string propertyName)
    {
        var value = node?[propertyName];
        if (value is null)
            return null;

        if (value.GetValueKind() == JsonValueKind.True)
            return true;

        if (value.GetValueKind() == JsonValueKind.False)
            return false;

        if (bool.TryParse(value.ToString(), out var parsed))
            return parsed;

        return null;
    }

    private static string TryGetString(JsonNode node, string propertyName)
    {
        var value = node?[propertyName];
        return value?.ToString();
    }

    private static string TrimDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return null;

        return details.Length <= 2048 ? details : details[..2048];
    }
}
