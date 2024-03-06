using ExCSS;
using Valour.Server.Database;
using Valour.Server.Mapping.Themes;
using Valour.Server.Models.Themes;
using Valour.Server.Utilities;
using Valour.Shared;

namespace Valour.Server.Services;

public class ThemeService
{
    private readonly ValourDB _db;
    private readonly ILogger<ThemeService> _logger;
    private readonly StylesheetParser _parser = new();
    
    public ThemeService(ValourDB db, ILogger<ThemeService> logger)
    {
        _db = db;
        _logger = logger;
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
    
    /// <summary>
    /// Adds the given theme to the database.
    /// </summary>
    /// <param name="theme">The theme to add</param>
    /// <returns>A task result with the created theme</returns>
    public async Task<TaskResult<Theme>> CreateTheme(Theme theme)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        
        try {
            
            // Validate CSS on theme
            if (!string.IsNullOrWhiteSpace(theme.CustomCss))
            {
                if (theme.CustomCss.Contains('[') || theme.CustomCss.Contains(']'))
                {
                    return TaskResult<Theme>.FromError("CSS contains disallowed characters []. Attribute selectors are not allowed in custom CSS for security reasons.");
                }
                
                // Parse valid CSS and write it back to strip anything malicious
                var css = await _parser.ParseAsync(theme.CustomCss);
                theme.CustomCss = css.ToCss();
            }
            
            // Validate all colors
            var colorsValid = ColorHelpers.ValidateColorCode(theme.FontColor)
                              && ColorHelpers.ValidateColorCode(theme.FontAltColor)
                              && ColorHelpers.ValidateColorCode(theme.LinkColor)
                              && ColorHelpers.ValidateColorCode(theme.MainColor1)
                              && ColorHelpers.ValidateColorCode(theme.MainColor2)
                              && ColorHelpers.ValidateColorCode(theme.MainColor3)
                              && ColorHelpers.ValidateColorCode(theme.MainColor4)
                              && ColorHelpers.ValidateColorCode(theme.MainColor5)
                              && ColorHelpers.ValidateColorCode(theme.TintColor)
                              && ColorHelpers.ValidateColorCode(theme.VibrantPurple)
                              && ColorHelpers.ValidateColorCode(theme.VibrantBlue)
                              && ColorHelpers.ValidateColorCode(theme.VibrantCyan)
                              && ColorHelpers.ValidateColorCode(theme.PastelCyan)
                              && ColorHelpers.ValidateColorCode(theme.PastelCyanPurple)
                              && ColorHelpers.ValidateColorCode(theme.PastelPurple)
                              && ColorHelpers.ValidateColorCode(theme.PastelRed);
            
            if (!colorsValid)
            {
                return TaskResult<Theme>.FromError("One or more color codes are invalid.");
            }

            if (string.IsNullOrWhiteSpace(theme.Name))
            {
                return TaskResult<Theme>.FromError("Theme name is required.");
            }
            
            if (theme.Name.Length > 50)
            {
                return TaskResult<Theme>.FromError("Theme name is too long.");
            }
            
            if (!string.IsNullOrWhiteSpace(theme.Description))
            {
                if (theme.Description.Length > 500)
                {
                    return TaskResult<Theme>.FromError("Theme description is too long. Limit 500 characters.");
                }
            }

            // This must be set through api
            theme.ImageUrl = null;
            
            // Themes start unpublished
            theme.Published = false;

            if (!await _db.Users.AnyAsync(x => x.Id == theme.AuthorId))
            {
                return TaskResult<Theme>.FromError("Author does not exist.");
            }

            var dbTheme = theme.ToDatabase();
            dbTheme.Id = IdManager.Generate();
            
            _db.Themes.Add(dbTheme);
            await _db.SaveChangesAsync();
            
            await transaction.CommitAsync();

            return TaskResult<Theme>.FromData(dbTheme.ToModel());
        } catch (Exception e) {
            await transaction.RollbackAsync();
            _logger.LogError("Failed to create theme", e);
            return TaskResult<Theme>.FromError("An error occured saving theme to the database.");
        }
    }
}