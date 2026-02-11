using Microsoft.Extensions.Logging;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.ModelLogic.QueryEngines;
using Valour.Sdk.Models.Themes;
using Valour.Shared.Queries;

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
    
    public ThemeMetaQueryEngine GetAvailableThemeReader(int amount = 20, string search = null)
    {
        var reader = new ThemeMetaQueryEngine(_client.PrimaryNode, amount);
        
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

        // Convert server-side ThemeMeta to client-side ThemeMeta
        return response.Data.Select(serverMeta => new ThemeMeta()
        {
            Id = serverMeta.Id,
            AuthorId = serverMeta.AuthorId,
            Name = serverMeta.Name,
            Description = serverMeta.Description,
            HasCustomBanner = serverMeta.HasCustomBanner,
            HasAnimatedBanner = serverMeta.HasAnimatedBanner,
            MainColor1 = serverMeta.MainColor1,
            PastelCyan = serverMeta.PastelCyan,
            AuthorName = serverMeta.AuthorName,
            Upvotes = serverMeta.Upvotes,
            Downvotes = serverMeta.Downvotes
        }).ToList();
    }
}