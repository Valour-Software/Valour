using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Services;

public class TenorFavoriteService
{
    /// <summary>
    /// Favorites are a personal shortlist, so each user keeps a bounded number.
    /// </summary>
    public const int MaxFavoritesPerUser = 500;

    private static readonly Regex TenorIdRegex = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);

    private readonly ValourDb _db;
    private readonly ILogger<TenorFavoriteService> _logger;

    public TenorFavoriteService(
        ValourDb db,
        ILogger<TenorFavoriteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenorFavorite> GetAsync(long id) =>
        (await _db.TenorFavorites.FindAsync(id)).ToModel();

    public async Task<TaskResult<TenorFavorite>> CreateAsync(TenorFavorite tenorFavorite)
    {
        if (tenorFavorite is null || !TenorIdRegex.IsMatch(tenorFavorite.TenorId ?? string.Empty))
            return TaskResult<TenorFavorite>.FromFailure("Invalid Tenor identifier.");

        if (await _db.TenorFavorites.AnyAsync(x =>
                x.UserId == tenorFavorite.UserId && x.TenorId == tenorFavorite.TenorId))
            return TaskResult<TenorFavorite>.FromFailure("That GIF is already in your favorites.", 409);

        if (await _db.TenorFavorites.CountAsync(x => x.UserId == tenorFavorite.UserId) >= MaxFavoritesPerUser)
            return TaskResult<TenorFavorite>.FromFailure(
                $"You can keep up to {MaxFavoritesPerUser} favorite GIFs. Remove one to add another.");

        tenorFavorite.Id = IdManager.Generate();
        try
        {
            _db.TenorFavorites.Add(tenorFavorite.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e, "Failed to create Tenor favorite.");
            return TaskResult<TenorFavorite>.FromFailure("Could not save GIF favorite.");
        }

        return new(true, "Success", tenorFavorite);
    }

    public async Task<TaskResult> DeleteAsync(TenorFavorite tenorFavorite)
    {
        try
        {
            var entity = await _db.TenorFavorites.FindAsync(tenorFavorite.Id);
            if (entity is null)
                return TaskResult.FromFailure("Tenor favorite not found.", 404);

            _db.TenorFavorites.Remove(entity);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e, "Failed to delete Tenor favorite.");
            return TaskResult.FromFailure("Could not remove GIF favorite.");
        }

        return new(true, "Success");
    }
}
