using ExCSS;
using Valour.Server.Database;
using Valour.Server.Mapping.Themes;
using Valour.Server.Models.Themes;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Themes;

namespace Valour.Server.Services;

public class ThemeService
{
    private readonly ValourDb _db;
    private readonly ILogger<ThemeService> _logger;
    private readonly StylesheetParser _parser = new();

    public ThemeService(ValourDb db, ILogger<ThemeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns the theme with the given id
    /// </summary>
    /// <param name="id">The id of the theme</param>
    /// <returns>The theme</returns>
    public async Task<Theme> GetTheme(long id) =>
        (await _db.Themes.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns a list of theme meta info, with optional search and pagination.
    /// </summary>
    /// <param name="take">The number of themes to return in the page</param>
    /// <param name="page">The page to return</param>
    /// <param name="search">Search query</param>
    /// <returns>A list of theme meta info</returns>
    public async Task<QueryResponse<ThemeMeta>> GetThemes(int skip = 0, int take = 20, string search = null)
    {
        if (take > 50)
            take = 50;
        
        var baseQuery = _db.Themes
            .AsNoTracking()
            .Where(x => x.Published);
            
        if (!string.IsNullOrWhiteSpace(search))
        {
            baseQuery = baseQuery.Where(x => x.Name.ToLower().Contains(search.ToLower()));
        }
            
            
        var mainQuery = baseQuery
            .Include(x => x.ThemeVotes)
            .Select(x => new
            {
                Theme = x,
                VoteCount = x.ThemeVotes.Count(v => v.Sentiment) - 
                            x.ThemeVotes.Count(v => !v.Sentiment)
            })
            .OrderByDescending(x => x.VoteCount)
            .Skip(take * page)
            .Take(take);
        
        var count = await mainQuery.CountAsync();
        
        var data = await mainQuery.Select(x => new ThemeMeta()
        {
            Id = x.Theme.Id,
            AuthorId = x.Theme.AuthorId,
            Name = x.Theme.Name,
            Description = x.Theme.Description,
            HasCustomBanner = x.Theme.HasCustomBanner,
            HasAnimatedBanner = x.Theme.HasAnimatedBanner,
            MainColor1 = x.Theme.MainColor1,
            PastelCyan = x.Theme.PastelCyan
        }).ToListAsync();
        
        return new QueryResponse<ThemeMeta>()
        {
            Items = data,
            TotalCount = count
        };
    }

    public async Task<List<ThemeMeta>> GetThemesByUser(long userId)
    {
        return await _db.Themes.Where(x => x.AuthorId == userId).Select(x => new ThemeMeta()
        {
            Id = x.Id,
            AuthorId = x.AuthorId,
            Name = x.Name,
            Description = x.Description,
            HasCustomBanner = x.HasCustomBanner,
            HasAnimatedBanner = x.HasAnimatedBanner,
            MainColor1 = x.MainColor1,
            PastelCyan = x.PastelCyan
        }).ToListAsync();
    }



    private async Task<TaskResult> ValidateTheme(Theme theme)
    {
        // Validate CSS on theme
        if (!string.IsNullOrWhiteSpace(theme.CustomCss))
        {
            if (theme.CustomCss.Contains('[') || theme.CustomCss.Contains(']'))
            {
                return TaskResult.FromFailure(
                    "CSS contains disallowed characters []. Attribute selectors are not allowed in custom CSS for security reasons.");
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
            return TaskResult.FromFailure("One or more color codes are invalid.");
        }

        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            return TaskResult.FromFailure("Theme name is required.");
        }

        if (theme.Name.Length > 50)
        {
            return TaskResult.FromFailure("Theme name is too long.");
        }

        if (!string.IsNullOrWhiteSpace(theme.Description))
        {
            if (theme.Description.Length > 500)
            {
                return TaskResult.FromFailure("Theme description is too long. Limit 500 characters.");
            }
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Updates the given theme in the database.
    /// </summary>
    /// <param name="updated">The updated version of the theme</param>
    /// <returns>A task result with the updated theme</returns>
    public async Task<TaskResult<Theme>> UpdateTheme(Theme updated)
    {
        var old = await _db.Themes.FindAsync(updated.Id);
        if (old is null)
            return TaskResult<Theme>.FromFailure("Theme not found");

        var validation = await ValidateTheme(updated);
        if (!validation.Success)
            return new TaskResult<Theme>(false, validation.Message);

        if (updated.AuthorId != old.AuthorId)
        {
            return TaskResult<Theme>.FromFailure("Cannot change author of theme.");
        }

        if (updated.HasCustomBanner != old.HasCustomBanner ||
            updated.HasAnimatedBanner != old.HasAnimatedBanner)
        {
            return TaskResult<Theme>.FromFailure("Cannot change custom banner status of theme. Use separate endpoint.");
        }

        var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            _db.Themes.Update(old);
            await _db.SaveChangesAsync();
            await trans.CommitAsync();

            return TaskResult<Theme>.FromData(updated);
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("Failed to update theme", e);
            return TaskResult<Theme>.FromFailure("An error occured updating theme in the database.");
        }
    }

    /// <summary>
    /// Adds the given theme to the database.
    /// </summary>
    /// <param name="theme">The theme to add</param>
    /// <returns>A task result with the created theme</returns>
    public async Task<TaskResult<Theme>> CreateTheme(Theme theme)
    {
        // Limit users to 20 themes for now
        var themeCount = await _db.Themes
            .Where(x => x.AuthorId == theme.AuthorId)
            .CountAsync();
        
        if (themeCount >= 20)
        {
            return TaskResult<Theme>.FromFailure("You have reached the maximum number of created themes.");
        }
        
        using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var validation = await ValidateTheme(theme);
            if (!validation.Success)
                return new TaskResult<Theme>(false, validation.Message);

            // Themes start unpublished (ok but why did i even do this)
            // theme.Published = false;

            if (!await _db.Users.AnyAsync(x => x.Id == theme.AuthorId))
            {
                return TaskResult<Theme>.FromFailure("Author does not exist.");
            }

            var dbTheme = theme.ToDatabase();
            dbTheme.Id = IdManager.Generate();

            _db.Themes.Add(dbTheme);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            return TaskResult<Theme>.FromData(dbTheme.ToModel());
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            _logger.LogError("Failed to create theme", e);
            return TaskResult<Theme>.FromFailure("An error occured saving theme to the database.");
        }
    }

    /// <summary>
    /// Deletes the theme with the given id
    /// </summary>
    /// <param name="id">The id of the theme to delete</param>
    /// <returns>A task result</returns>
    public async Task<TaskResult> DeleteTheme(long id)
    {
        var existing = await _db.Themes.FindAsync(id);
        if (existing is null)
            return TaskResult.FromFailure("Theme not found");

        var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Themes.Remove(existing);
            await _db.SaveChangesAsync();
            await trans.CommitAsync();

            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("Failed to delete theme", e);
            return TaskResult.FromFailure("An error occured removing theme from the database.");
        }
    }

    /// <summary>
    /// Creates the given theme vote on the database. Will also overwrite an existing vote.
    /// </summary>
    /// <param name="vote">The theme vote to create</param>
    /// <returns>A task result with the created vote</returns>
    public async Task<TaskResult<ThemeVote>> CreateThemeVote(ThemeVote vote)
    {
        var existing = await _db.ThemeVotes
            .Where(x => x.ThemeId == vote.ThemeId && x.UserId == vote.UserId)
            .FirstOrDefaultAsync();

        var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            if (existing is not null)
            {
                if (existing.Sentiment == vote.Sentiment)
                    return TaskResult<ThemeVote>.FromFailure("Vote already exists");

                // Logic for updating an existing vote
                existing.Sentiment = vote.Sentiment;
                existing.CreatedAt = DateTime.UtcNow;

                _db.ThemeVotes.Update(existing);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();

                return new TaskResult<ThemeVote>(true, "Vote updated", existing.ToModel());
            }

            if (!await _db.Users.AnyAsync(x => x.Id == vote.UserId))
                return TaskResult<ThemeVote>.FromFailure("User does not exist");

            if (!await _db.Themes.AnyAsync(x => x.Id == vote.ThemeId))
                return TaskResult<ThemeVote>.FromFailure("Theme does not exist");

            var dbVote = vote.ToDatabase();
            dbVote.Id = IdManager.Generate();

            _db.ThemeVotes.Add(dbVote);
            await _db.SaveChangesAsync();

            await trans.CommitAsync();
            
            return TaskResult<ThemeVote>.FromData(dbVote.ToModel());
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("Failed to create theme vote", e);
            return TaskResult<ThemeVote>.FromFailure("An error occured saving vote to the database.");
        }
    }

    /// <summary>
    /// Removes the theme vote with the given id from the database
    /// </summary>
    /// <param name="id">The id of the theme to remove</param>
    /// <returns>A task result with success or failure details</returns>
    public async Task<TaskResult> DeleteThemeVote(long id)
    {
        var existing = await _db.ThemeVotes.FindAsync(id);

        if (existing is null)
            return TaskResult.FromFailure("Vote not found");

        var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            _db.ThemeVotes.Remove(existing);
            await _db.SaveChangesAsync();

            await trans.CommitAsync();

            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            
            _logger.LogError("Failed to delete theme vote", e);
            return TaskResult.FromFailure("An error occured removing vote from the database.");
        }
    }

    public async Task<ThemeVoteTotals> GetThemeVotesAsync(long id)
    {
        // If a theme vote has Sentiment = true, then it is a upvote
        // If a theme vote has Sentiment = false, then it is a downvote
        var result = await _db.ThemeVotes
            .Where(x => x.ThemeId == id)
            .GroupBy(x => x.Sentiment)
            .Select(x => new
            {
                Sentiment = x.Key,
                Count = x.Count()
            })
            .ToListAsync();
        
        var upvotes = result.FirstOrDefault(x => x.Sentiment);
        var downvotes = result.FirstOrDefault(x => !x.Sentiment);
        
        var upvoteCount = upvotes?.Count ?? 0;
        var downvoteCount = downvotes?.Count ?? 0;

        return new ThemeVoteTotals()
        {
            Upvotes = upvoteCount,
            Downvotes = downvoteCount
        };
    }

    public async Task<ThemeVote> GetUserVote(long userId, long themeId)
    {
        return (await _db.ThemeVotes
            .Where(x => x.UserId == userId && x.ThemeId == themeId)
            .FirstOrDefaultAsync()).ToModel();
    }
}