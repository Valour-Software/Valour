using System.Net.Http.Json;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.TenorTwo;
using Valour.TenorTwo.Models;
using Valour.TenorTwo.Responses;

namespace Valour.Sdk.Services;

public class TenorService : ServiceBase
{
    /// <summary>
    /// The key Valour used for the (now shut down) Tenor API. Kept only because the
    /// Tenor v2 request URLs still need *a* key value in them for KlipyTenorCompatHandler
    /// to find-and-replace with the real Klipy key - see that class for details.
    /// </summary>
    public const string LegacyTenorKey = "AIzaSyCpYasE9IZNecc7ZPEjHTpOVssJT1aUC_4";

    /// <summary>
    /// Klipy API key, from https://klipy.com/developers. Fill this in to enable GIFs
    /// again now that Tenor's API is gone.
    /// </summary>
    public const string KlipyApiKey = "hU08WDX71E2Wm3fgC5jKrtubVtmjwlao2aq6PpIz0KGFfGUdnAaGd8Xth77m8dxP";

    /// <summary>
    /// Client for interacting with the Tenor API
    /// </summary>
    public TenorClient Client => _tenor;
    
    private readonly HttpClient _httpClient;
    
    /// <summary>
    /// The Tenor favorites of this user
    /// </summary>
    public readonly IReadOnlyList<TenorFavorite> TenorFavorites;
    private List<TenorFavorite> _tenorFavorites = new();
    
    public static readonly List<MediaFormatType> Formats = new()
    {
        MediaFormatType.gif,
        MediaFormatType.tinygif
    };

    // Formats never changes at runtime, so this is computed once instead of re-joining
    // it on every GetFavoritePostsAsync call.
    private static readonly string FormatsQueryValue = string.Join(',', Formats);
    
    private readonly LogOptions _logOptions = new(
        "PlanetService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    private readonly TenorClient _tenor;
    
    public TenorService(HttpClient httpClient, ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
        
        _httpClient = httpClient;
        TenorFavorites = _tenorFavorites;
        _tenor = new TenorClient(LegacyTenorKey, "valour", http: httpClient);
    }
    
    public HttpClient GetHttpClient()
    {
        return _httpClient;
    }
    
    public async Task LoadTenorFavoritesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<TenorFavorite>>("api/users/me/tenorfavorites");
        if (!response.Success)
        {
            LogError("Failed to load Tenor favorites", response);
            return;
        }
        
        _tenorFavorites.Clear();
        foreach (var fav in response.Data)
        {
            fav.SetClient(_client);
            _tenorFavorites.Add(fav);
        }

        Log($"Loaded {TenorFavorites.Count} Tenor favorites");
    }
    
    /// <summary>
    /// Tries to add the given Tenor favorite
    /// </summary>
    public async Task<TaskResult<TenorFavorite>> AddTenorFavorite(TenorFavorite favorite)
    {
        var result = await favorite.CreateAsync();

        if (result.Success)
            _tenorFavorites.Add(result.Data);

        return result;
    }

    /// <summary>
    /// Fetches the media for all saved favorites. Works around a bug in TenorTwo where
    /// Posts() incorrectly hits the /search endpoint instead of /posts.
    /// </summary>
    public async Task<MediaResponse> GetFavoritePostsAsync()
    {
        if (_tenorFavorites.Count == 0)
            return null;

        // Klipy validates each id as a standard signed 64-bit integer. Legacy Tenor ids
        // that overflow that range (Tenor treated ids as arbitrary-precision numbers) get
        // rejected with a 422 for the whole batch, so they're dropped here instead. Ids
        // from before the Klipy migration also won't resolve even when they do fit - Klipy
        // has its own separately-sourced GIF catalog rather than a live mirror of Tenor's,
        // so those favorites just come back silently absent from the results.
        var validIds = new List<string>(_tenorFavorites.Count);
        foreach (var fav in _tenorFavorites)
        {
            if (long.TryParse(fav.TenorId, out _))
                validIds.Add(fav.TenorId);
        }

        if (validIds.Count == 0)
            return new MediaResponse { Results = Array.Empty<Media>() };

        if (validIds.Count < _tenorFavorites.Count)
        {
            LogWarning($"Skipping {_tenorFavorites.Count - validIds.Count} favorite(s) with ids too large for Klipy's integer validation.");
        }

        var ids = string.Join(',', validIds);
        var url = $"https://tenor.googleapis.com/v2/posts?key={LegacyTenorKey}&ids={ids}&client_key=valour&media_filter={FormatsQueryValue}&limit=100";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            LogError($"Favorites lookup failed ({(int)response.StatusCode} {response.StatusCode}) for request " +
                     $"{response.RequestMessage?.RequestUri}\nResponse body: {body}");
            return null;
        }

        return await response.Content.ReadFromJsonAsync<MediaResponse>();
    }

    /// <summary>
    /// Tries to delete the given Tenor favorite
    /// </summary>
    public async Task<TaskResult> RemoveTenorFavorite(TenorFavorite favorite)
    {
        var result = await favorite.DeleteAsync();

        if (result.Success)
            _tenorFavorites.RemoveAll(x => x.Id == favorite.Id);

        return result;
    }
}