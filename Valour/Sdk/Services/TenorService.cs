using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class TenorService : ServiceBase
{
    /// <summary>
    /// The Tenor favorites of this user
    /// </summary>
    public readonly IReadOnlyList<TenorFavorite> TenorFavorites;
    private List<TenorFavorite> _tenorFavorites = new();
    
    private readonly LogOptions _logOptions = new(
        "PlanetService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    
    public TenorService(ValourClient client)
    {
        _client = client;
        
        SetupLogging(client.Logger, _logOptions);
        
        TenorFavorites = _tenorFavorites;
    }
    
    public async Task LoadTenorFavoritesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<TenorFavorite>>("api/users/self/tenorfavorites");
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
        var result = await TenorFavorite.PostAsync(favorite);

        if (result.Success)
            _tenorFavorites.Add(result.Data);

        return result;
    }

    /// <summary>
    /// Tries to delete the given Tenor favorite
    /// </summary>
    public async Task<TaskResult> RemoveTenorFavorite(TenorFavorite favorite)
    {
        var result = await TenorFavorite.DeleteAsync(favorite);

        if (result.Success)
            _tenorFavorites.RemoveAll(x => x.Id == favorite.Id);

        return result;
    }
}