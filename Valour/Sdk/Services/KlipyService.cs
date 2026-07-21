using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// Direct client integration with Klipy. Klipy's platform key is intentionally
/// public to the application, so it must be supplied as runtime client
/// configuration and must never be reused as a server credential.
/// </summary>
public class KlipyService : ServiceBase
{
    private const int MaxSearchLength = 160;
    private const int MaxPage = 10_000;
    private const int PerPage = 50;
    private const string ApiBaseAddress = "https://api.klipy.com/api/v1/";
    private const string CustomerId = "valour";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ValourClient _client;
    private readonly HttpClient _http;
    private readonly List<GifFavorite> _gifFavorites = new();

    public readonly IReadOnlyList<GifFavorite> GifFavorites;

    /// <summary>
    /// Whether a host supplied a public Klipy platform key.
    /// </summary>
    public bool IsConfigured { get; private set; }

    public KlipyService(ValourClient client, HttpClient? http = null)
    {
        _client = client;
        _http = http ?? new HttpClient();
        GifFavorites = _gifFavorites;
        SetupLogging(client.Logger, new LogOptions("KlipyService", "#3381a3", "#a3333e", "#a39433"));
    }

    /// <summary>
    /// Applies the host's public Klipy platform key. Supplying an empty value
    /// disables provider calls without affecting stored favorites.
    /// </summary>
    public void Configure(string? publicApiKey)
    {
        var key = publicApiKey?.Trim();
        IsConfigured = !string.IsNullOrWhiteSpace(key);
        _http.BaseAddress = IsConfigured
            ? new Uri(ApiBaseAddress + Uri.EscapeDataString(key!) + "/", UriKind.Absolute)
            : null;
    }

    public async Task LoadGifFavoritesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<GifFavorite>>("api/users/me/giffavorites");
        if (!response.Success)
        {
            LogError("Failed to load GIF favorites", response);
            return;
        }

        ApplyGifFavorites(response.Data);
    }

    public void ApplyGifFavorites(IEnumerable<GifFavorite> favorites)
    {

        _gifFavorites.Clear();
        _gifFavorites.AddRange((favorites ?? []).Select(x => x.Sync(_client)));
    }

    public async Task<TaskResult<KlipyGifSearchResults>> SearchAsync(string? query, int page = 1)
    {
        if (!IsConfigured)
            return TaskResult<KlipyGifSearchResults>.FromFailure("GIF search is not configured.", 503);

        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxSearchLength)
            return TaskResult<KlipyGifSearchResults>.FromFailure(
                $"Search text must be between 1 and {MaxSearchLength} characters.", 400);

        if (page is < 1 or > MaxPage)
            return TaskResult<KlipyGifSearchResults>.FromFailure("Invalid GIF search page.", 400);

        var providerResult = await GetProviderAsync<ProviderSearchPage>(
            $"gifs/search?q={Uri.EscapeDataString(query)}&page={page}&per_page={PerPage}");
        if (!providerResult.Success)
            return TaskResult<KlipyGifSearchResults>.FromFailure(providerResult);

        return TaskResult<KlipyGifSearchResults>.FromData(new KlipyGifSearchResults
        {
            Results = providerResult.Data.Data
                .Select(NormalizeGif)
                .Where(x => x is not null)
                .Cast<KlipyGif>()
                .ToList(),
            HasNext = providerResult.Data.HasNext
        });
    }

    public async Task<TaskResult<List<KlipyCategory>>> GetCategoriesAsync()
    {
        if (!IsConfigured)
            return TaskResult<List<KlipyCategory>>.FromFailure("GIF search is not configured.", 503);

        var providerResult = await GetProviderAsync<ProviderCategoriesResult>("gifs/categories");
        if (!providerResult.Success)
            return TaskResult<List<KlipyCategory>>.FromFailure(providerResult);

        return TaskResult<List<KlipyCategory>>.FromData(providerResult.Data.Categories
            .Where(c => IsSafeCategory(c.Category) && KlipyMediaUrls.IsAllowed(c.PreviewUrl))
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(50)
            .Select(c => new KlipyCategory
            {
                Name = c.Category,
                SearchTerm = string.IsNullOrWhiteSpace(c.Query) ? c.Category : c.Query,
                Image = c.PreviewUrl
            })
            .ToList());
    }

    /// <summary>
    /// Reports a selected GIF to Klipy without disclosing a Valour user ID.
    /// The customer identifier intentionally identifies the app only.
    /// </summary>
    public async Task<TaskResult> RegisterShareAsync(string? slug)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("GIF search is not configured.", 503);

        if (!IsSafeSlug(slug))
            return TaskResult.FromFailure("Invalid GIF identifier.", 400);

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"gifs/share/{Uri.EscapeDataString(slug)}",
                new { customer_id = CustomerId });

            return response.IsSuccessStatusCode
                ? TaskResult.SuccessResult
                : TaskResult.FromFailure("GIF share registration failed.", (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return TaskResult.FromFailure("GIF share registration is unavailable.", 503);
        }
        catch (TaskCanceledException)
        {
            return TaskResult.FromFailure("GIF share registration timed out.", 504);
        }
    }

    public async Task<TaskResult<GifFavorite>> AddGifFavoriteAsync(GifFavorite favorite)
    {
        var result = await favorite.CreateAsync();
        if (result.Success)
            _gifFavorites.Add(result.Data.Sync(_client));

        return result;
    }

    public async Task<TaskResult> RemoveGifFavoriteAsync(GifFavorite favorite)
    {
        var result = await favorite.DeleteAsync();
        if (result.Success)
            _gifFavorites.RemoveAll(x => x.Id == favorite.Id);

        return result;
    }

    private async Task<TaskResult<T>> GetProviderAsync<T>(string path)
    {
        try
        {
            using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return TaskResult<T>.FromFailure("GIF provider is unavailable.", (int)response.StatusCode);

            var wrapper = await response.Content.ReadFromJsonAsync<ProviderResponse<T>>(JsonOptions);
            if (wrapper?.Result != true || wrapper.Data is null)
                return TaskResult<T>.FromFailure("GIF provider returned an invalid response.", 502);

            return TaskResult<T>.FromData(wrapper.Data);
        }
        catch (HttpRequestException)
        {
            return TaskResult<T>.FromFailure("GIF provider is unavailable.", 503);
        }
        catch (TaskCanceledException)
        {
            return TaskResult<T>.FromFailure("GIF provider timed out.", 504);
        }
        catch (JsonException)
        {
            return TaskResult<T>.FromFailure("GIF provider returned an invalid response.", 502);
        }
    }

    private static KlipyGif? NormalizeGif(KlipyGif source)
    {
        if (!IsSafeSlug(source.Slug))
            return null;

        source.Slug = source.Slug.Trim();
        source.Title = (source.Title ?? string.Empty).Trim()[..Math.Min((source.Title ?? string.Empty).Trim().Length, 300)];
        source.File = NormalizeFile(source.File);
        return source.Preview is not null && source.Gif is not null ? source : null;
    }

    private static KlipyMediaFile NormalizeFile(KlipyMediaFile? source) => new()
    {
        Hd = NormalizeFormats(source?.Hd),
        Md = NormalizeFormats(source?.Md),
        Sm = NormalizeFormats(source?.Sm),
        Xs = NormalizeFormats(source?.Xs)
    };

    private static KlipyMediaFormats? NormalizeFormats(KlipyMediaFormats? source)
    {
        if (source is null)
            return null;

        return new KlipyMediaFormats
        {
            Gif = NormalizeAsset(source.Gif),
            Webp = NormalizeAsset(source.Webp),
            Mp4 = NormalizeAsset(source.Mp4)
        };
    }

    private static KlipyMediaAsset? NormalizeAsset(KlipyMediaAsset? source)
    {
        if (source is null || !KlipyMediaUrls.IsAllowed(source.Url) ||
            source.Width is < 1 or > 16_384 || source.Height is < 1 or > 16_384 ||
            source.Size is < 0 or > 100_000_000)
            return null;

        return source;
    }

    private static bool IsSafeSlug(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
        value.All(c => char.IsLetterOrDigit(c) || c == '-');

    private static bool IsSafeCategory(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_');

    private sealed class ProviderResponse<T>
    {
        [JsonPropertyName("result")]
        public bool Result { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class ProviderSearchPage
    {
        [JsonPropertyName("data")]
        public List<KlipyGif> Data { get; set; } = new();

        [JsonPropertyName("has_next")]
        public bool HasNext { get; set; }
    }

    private sealed class ProviderCategoriesResult
    {
        [JsonPropertyName("categories")]
        public List<ProviderCategory> Categories { get; set; } = new();
    }

    private sealed class ProviderCategory
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("preview_url")]
        public string PreviewUrl { get; set; } = string.Empty;
    }
}
