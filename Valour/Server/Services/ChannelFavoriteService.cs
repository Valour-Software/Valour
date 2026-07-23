using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using ChannelFavorite = Valour.Server.Models.ChannelFavorite;

namespace Valour.Server.Services;

public class ChannelFavoriteService
{
    /// <summary>
    /// Sanity cap. Favorites are a shortlist, not a second channel list.
    /// </summary>
    private const int MaxFavoritesPerPlanet = 100;

    private readonly ValourDb _db;
    private readonly ILogger<ChannelFavoriteService> _logger;

    public ChannelFavoriteService(ValourDb db, ILogger<ChannelFavoriteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<ChannelFavorite>> GetForUserAsync(long userId) =>
        await _db.ChannelFavorites
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.PlanetId)
            .ThenBy(x => x.Position)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task<TaskResult<ChannelFavorite>> CreateAsync(long userId, long channelId, long planetId)
    {
        if (channelId <= 0 || planetId <= 0)
            return TaskResult<ChannelFavorite>.FromFailure("Invalid channel favorite.");

        // Channels on federated planets may not exist in the local database, so
        // only validate against channels we actually know about.
        var localChannel = await _db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == channelId);
        if (localChannel is not null)
        {
            if (localChannel.PlanetId is null)
                return TaskResult<ChannelFavorite>.FromFailure("Only planet channels can be favorited.");

            if (localChannel.ChannelType == ChannelTypeEnum.PlanetCategory)
                return TaskResult<ChannelFavorite>.FromFailure("Categories cannot be favorited.");

            planetId = localChannel.PlanetId.Value;
        }

        var planetFavorites = _db.ChannelFavorites
            .Where(x => x.UserId == userId && x.PlanetId == planetId);

        if (await planetFavorites.CountAsync() >= MaxFavoritesPerPlanet)
            return TaskResult<ChannelFavorite>.FromFailure("You have too many favorites in this planet.");

        var maxPosition = await planetFavorites
            .Select(x => (int?)x.Position)
            .MaxAsync() ?? -1;

        var favorite = new ChannelFavorite
        {
            Id = IdManager.Generate(),
            UserId = userId,
            ChannelId = channelId,
            PlanetId = planetId,
            Position = maxPosition + 1
        };

        try
        {
            _db.ChannelFavorites.Add(favorite.ToDatabase());
            await _db.SaveChangesAsync();
            return TaskResult<ChannelFavorite>.FromData(favorite);
        }
        catch (DbUpdateException)
        {
            return TaskResult<ChannelFavorite>.FromFailure("That channel is already in your favorites.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create channel favorite.");
            return TaskResult<ChannelFavorite>.FromFailure("Could not save channel favorite.");
        }
    }

    public async Task<TaskResult> DeleteAsync(long userId, long channelId)
    {
        try
        {
            var entity = await _db.ChannelFavorites
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId);
            if (entity is null)
                return TaskResult.FromFailure("Channel favorite not found.", 404);

            _db.ChannelFavorites.Remove(entity);
            await _db.SaveChangesAsync();
            return TaskResult.SuccessResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete channel favorite.");
            return TaskResult.FromFailure("Could not remove channel favorite.");
        }
    }

    /// <summary>
    /// Applies the given channel order to the user's favorites within one
    /// planet. Favorites missing from the list keep their relative order after
    /// the listed ones.
    /// </summary>
    public async Task<TaskResult> ReorderAsync(long userId, long planetId, List<long> channelIds)
    {
        if (channelIds is null || channelIds.Count > MaxFavoritesPerPlanet)
            return TaskResult.FromFailure("Invalid favorite order.");

        try
        {
            var favorites = await _db.ChannelFavorites
                .Where(x => x.UserId == userId && x.PlanetId == planetId)
                .OrderBy(x => x.Position)
                .ToListAsync();

            var next = 0;
            var seen = new HashSet<long>();
            foreach (var channelId in channelIds)
            {
                if (!seen.Add(channelId))
                    continue;

                var favorite = favorites.FirstOrDefault(x => x.ChannelId == channelId);
                if (favorite is null)
                    continue;

                favorite.Position = next++;
            }

            foreach (var favorite in favorites)
            {
                if (!seen.Contains(favorite.ChannelId))
                    favorite.Position = next++;
            }

            await _db.SaveChangesAsync();
            return TaskResult.SuccessResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorder channel favorites.");
            return TaskResult.FromFailure("Could not reorder channel favorites.");
        }
    }
}
