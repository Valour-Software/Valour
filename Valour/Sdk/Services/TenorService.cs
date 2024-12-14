using Valour.Sdk.Client;
using Valour.Shared;
using Valour.TenorTwo;
using Valour.TenorTwo.Models;

namespace Valour.Sdk.Services;

public class TenorService : ServiceBase
{
    /// <summary>
    /// The key for Valour using the Tenor API
    /// </summary>
    private const string TenorKey = "AIzaSyCpYasE9IZNecc7ZPEjHTpOVssJT1aUC_4";

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
        _tenor = new TenorClient(TenorKey, "valour", http: httpClient);
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
        _tenorFavorites.AddRange(response.Data);
        
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