using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Cdn;
using GifFavorite = Valour.Server.Models.GifFavorite;

namespace Valour.Server.Services;

public class GifFavoriteService
{
    private const string KlipyProvider = "klipy";
    private static readonly Regex ProviderIdRegex = new("^[A-Za-z0-9-]{1,160}$", RegexOptions.Compiled);
    private readonly ValourDb _db;
    private readonly ILogger<GifFavoriteService> _logger;

    public GifFavoriteService(ValourDb db, ILogger<GifFavoriteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<GifFavorite>> GetForUserAsync(long userId) =>
        await _db.GifFavorites
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task<GifFavorite?> GetAsync(long id) =>
        (await _db.GifFavorites.FindAsync(id)).ToModel();

    public async Task<TaskResult<GifFavorite>> CreateAsync(GifFavorite favorite)
    {
        var validation = Validate(favorite);
        if (!validation.Success)
            return TaskResult<GifFavorite>.FromFailure(validation);

        favorite.Id = IdManager.Generate();
        favorite.Provider = KlipyProvider;

        try
        {
            _db.GifFavorites.Add(favorite.ToDatabase());
            await _db.SaveChangesAsync();
            return TaskResult<GifFavorite>.FromData(favorite);
        }
        catch (DbUpdateException)
        {
            return TaskResult<GifFavorite>.FromFailure("That GIF is already in your favorites.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create GIF favorite.");
            return TaskResult<GifFavorite>.FromFailure("Could not save GIF favorite.");
        }
    }

    public async Task<TaskResult> DeleteAsync(GifFavorite favorite)
    {
        try
        {
            _db.GifFavorites.Remove(favorite.ToDatabase());
            await _db.SaveChangesAsync();
            return TaskResult.SuccessResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete GIF favorite.");
            return TaskResult.FromFailure("Could not remove GIF favorite.");
        }
    }

    private static TaskResult Validate(GifFavorite favorite)
    {
        if (favorite is null || !ProviderIdRegex.IsMatch(favorite.ProviderId ?? string.Empty))
            return TaskResult.FromFailure("Invalid GIF identifier.");

        if (favorite.Title?.Length > 300 ||
            !KlipyMediaUrls.IsAllowed(favorite.PreviewUrl) ||
            !KlipyMediaUrls.IsAllowed(favorite.GifUrl) ||
            favorite.Width is < 1 or > 16_384 || favorite.Height is < 1 or > 16_384)
            return TaskResult.FromFailure("Invalid GIF favorite.");

        return TaskResult.SuccessResult;
    }
}
