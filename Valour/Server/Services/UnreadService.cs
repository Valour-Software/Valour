using Valour.Shared.Models;

namespace Valour.Server.Services;

public class UnreadService
{
    private readonly ValourDb _db;
    private readonly ILogger<UnreadService> _logger;

    public UnreadService(ValourDb db, ILogger<UnreadService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns all channels with unread messages for a user
    /// If planet is null, returns all unread direct message channels
    /// </summary>
    public async Task<long[]> GetUnreadChannels(long? planetId, long userId)
    {
        return await _db.Channels
            .AsNoTracking()
            .Where(c => c.PlanetId == planetId)
            .Where(c => !_db.UserChannelStates
                .Where(s => s.UserId == userId)
                .Any(s => s.ChannelId == c.Id && s.LastViewedTime >= c.LastUpdateTime)
            )
            .Select(c => c.Id)
            .ToArrayAsync();
    }
    
    public async Task<long[]> GetUnreadPlanets(long userId)
    {
        return await _db.Channels
            .AsNoTracking()
            .Where(c => c.PlanetId != null)
            .Where(c => !_db.UserChannelStates
                .Where(s => s.UserId == userId)
                .Any(s => s.ChannelId == c.Id && s.LastViewedTime >= c.LastUpdateTime)
            )
            .Select(c => c.PlanetId.Value)
            .Distinct()
            .ToArrayAsync();
    }
    
    public async Task<UserChannelState> UpdateReadState(long channelId, long userId, DateTime? updateTime)
    {
        updateTime ??= DateTime.UtcNow;

        var channelState = await _db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId);

        if (channelState is null)
        {
            channelState = new UserChannelState()
            {
                UserId = userId,
                ChannelId = channelId
            }.ToDatabase();

            _db.UserChannelStates.Add(channelState);
        }
            
        channelState.LastViewedTime = updateTime.Value;
        await _db.SaveChangesAsync();

        return channelState.ToModel();
    }
}