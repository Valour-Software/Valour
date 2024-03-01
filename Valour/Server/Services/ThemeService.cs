using Valour.Server.Mapping.Themes;
using Valour.Server.Models.Themes;

namespace Valour.Server.Services;

public class ThemeService
{
    private readonly ValourDB _db;
    
    public ThemeService(ValourDB db)
    {
        _db = db;
    }
    
    public async Task<Theme> GetTheme(long id)
    {
        var theme = await _db.Themes.FindAsync(id);
        return theme.ToModel();
    }
    
    /// <summary>
    /// Returns a list of theme meta info, with optional search and pagination.
    /// </summary>
    /// <param name="amount">The number of themes to return in the page</param>
    /// <param name="page">The page to return</param>
    /// <param name="search">Search query</param>
    /// <returns>A list of theme meta info</returns>
    public async Task<List<ThemeMeta>> GetThemes(int amount = 20, int page = 0, string search = null)
    {
        var baseQuery = _db.Themes
            .Skip(amount * page)
            .Take(amount);

        if (search != null)
        {
            baseQuery = baseQuery.Where(x => x.Name.Contains(search, StringComparison.InvariantCultureIgnoreCase));
        }
            
        return await baseQuery.Select(x => new ThemeMeta()
        {
            Id = x.Id,
            AuthorId = x.AuthorId,
            Name = x.Name,
            Description = x.Description,
            ImageUrl = x.ImageUrl
        }).ToListAsync();   
    }
}