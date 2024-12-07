using Microsoft.Extensions.Logging;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Themes;

namespace Valour.Sdk.Services;

public class ThemeService : ServiceBase
{
    private static readonly LogOptions LogOptions = new (
        "ThemeService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );
    
    private readonly ValourClient _client;

    public ThemeService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    // No need to cache themes
    public async Task<Theme> FetchThemeAsync(long id)
    {
        var response = await _client.PrimaryNode.GetJsonAsync<Theme>($"api/themes/{id}");
        if (!response.Success)
        {
            LogWarning($"Failed to get theme: {response.Message}");
            return null;
        }

        return response.Data;
    }
    
    public PagedReader<ThemeMeta> GetAvailableThemeReader(int amount = 20, string search = null)
    {
        var query = new Dictionary<string, string>() {{ "search", search }}; 
        var reader = new PagedReader<ThemeMeta>(_client.PrimaryNode, "api/themes", amount, query);
        return reader;
    }
    
    public async Task<List<ThemeMeta>> GetMyThemes()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<ThemeMeta>>("api/themes/me");
        if (!response.Success)
        {
            LogWarning($"Failed to get my themes: {response.Message}");
            return new List<ThemeMeta>();
        }

        return response.Data;
    }
}