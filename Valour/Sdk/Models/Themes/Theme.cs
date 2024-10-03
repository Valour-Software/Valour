using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Themes;

namespace Valour.Sdk.Models.Themes;

public class Theme : LiveModel, ISharedTheme
{
    public override string BaseRoute => "api/themes";

    public static Theme Default = new Theme()
    {
        Id = 0,
        AuthorId = ISharedUser.VictorUserId,
        Name = "To The Stars (Default)",
        Description = "The default theme for Valour. Designed to be modern, sleek, and easy on the eyes.",
        
        FontColor = "#ffffff",
        FontAltColor = "#7a7a7a",
        LinkColor = "#00aaff",
        
        MainColor1 = "#040d14",
        MainColor2 = "#0b151d",
        MainColor3 = "#121e27",
        MainColor4 = "#182631",
        MainColor5 = "#212f3a",
        
        TintColor = "#ffffff",
        
        VibrantPurple = "#bf06fd",
        VibrantBlue = "#0c06fd",
        VibrantCyan = "#00faff",
        
        PastelCyan = "#37a4ce",
        PastelCyanPurple = "#6278cd",
        PastelPurple = "#8457cd",
        PastelRed = "#cd5e5e",
    };
    
    public long AuthorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    
    public bool HasCustomBanner { get; set; }
    
    public bool HasAnimatedBanner { get; set; }
    public bool Published { get; set; }
    
    public string FontColor { get; set; }
    public string FontAltColor { get; set; }
    public string LinkColor { get; set; }
    
    public string MainColor1 { get; set; }
    public string MainColor2 { get; set; }
    public string MainColor3 { get; set; }
    public string MainColor4 { get; set; }
    public string MainColor5 { get; set; }
    
    public string TintColor { get; set; }
    
    public string VibrantPurple { get; set; }
    public string VibrantBlue { get; set; }
    public string VibrantCyan { get; set; }
    
    public string PastelCyan { get; set; }
    public string PastelCyanPurple { get; set; }
    public string PastelPurple { get; set; }
    public string PastelRed { get; set; }
    
    public string CustomCss { get; set; }
    
    public ThemeMeta ToMeta()
    {
        return new ThemeMeta()
        {
            Id = Id,
            AuthorId = AuthorId,
            Name = Name,
            Description = Description,
            HasCustomBanner = HasCustomBanner,
            HasAnimatedBanner = HasAnimatedBanner,
            MainColor1 = MainColor1,
            PastelCyan = PastelCyan,
        };
    }
    
    // No need to cache themes
    public static async Task<Theme> FindAsync(long id)
    {
        var response = await ValourClient.GetJsonAsync<Theme>($"api/themes/{id}");
        if (!response.Success)
        {
            await Logger.Log($"Failed to get theme: {response.Message}", "yellow");
            return null;
        }

        return response.Data;
    }
    
    public static PagedReader<ThemeMeta> GetAvailableThemes(int amount = 20, int page = 0, string search = null)
    {
        Dictionary<string, string> query = new Dictionary<string, string>() {{"search", search}}; 
        var reader = new PagedReader<ThemeMeta>("api/themes", amount, query);

        return reader;
    }
    
    public static async Task<List<ThemeMeta>> GetMyThemes()
    {
        var response = await ValourClient.GetJsonAsync<List<ThemeMeta>>("api/themes/self");
        if (!response.Success)
        {
            await Logger.Log($"Failed to get self themes: {response.Message}", "yellow");
            return new List<ThemeMeta>();
        }

        return response.Data;
    }
    
    public async Task<ThemeVoteTotals> GetVoteTotals()
    {
        var response = await ValourClient.GetJsonAsync<ThemeVoteTotals>($"api/themes/{Id}/votes");
        if (!response.Success)
        {
            await Logger.Log($"Failed to get theme vote totals: {response.Message}", "yellow");
            return null;
        }

        return response.Data;
    }
    
    public async Task<ThemeVote> GetMyVote()
    {
        var response = await ValourClient.GetJsonAsync<ThemeVote>($"api/themes/{Id}/votes/self", true);
        if (!response.Success)
        {
            await Logger.Log($"Failed to get my theme vote: {response.Message}", "yellow");
            return null;
        }

        return response.Data;
    }

    public string GetBannerUrl(ThemeBannerFormat format) =>
        ISharedTheme.GetBannerUrl(this, format);
}