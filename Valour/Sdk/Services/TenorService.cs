using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.SDK.Services;

public class TenorService
{
    /// <summary>
    /// The Tenor favorites of this user
    /// </summary>
    public IReadOnlyList<TenorFavorite> TenorFavorites { get; private set; }
    private List<TenorFavorite> _tenorFavorites = new();
    
    private readonly ValourClient _client;
    
    public TenorService(ValourClient client)
    {
        _client = client;
        
        TenorFavorites = _tenorFavorites;
    }
    
    public async Task LoadTenorFavoritesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<TenorFavorite>>("api/users/self/tenorfavorites");
        if (!response.Success)
        {
            _client.Log("TenorService", "** Failed to load Tenor favorites **", "red");
            _client.Log("TenorService", response.Message, "red");
            return;
        }
        
        _tenorFavorites.Clear();
        _tenorFavorites.AddRange(response.Data);
        
        Console.WriteLine($"Loaded {TenorFavorites.Count} Tenor favorites");
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