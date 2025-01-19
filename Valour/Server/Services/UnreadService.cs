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
        var query =
            from c in _db.Channels.Where(x => x.PlanetId == planetId)
            join s in _db.UserChannelStates.Where(x => x.UserId == userId)
                on c.Id equals s.ChannelId
                into grouping
            from g in grouping.DefaultIfEmpty()
            where g == null || c.LastUpdateTime > g.LastViewedTime
            select c;

        return await query.Select(x => x.Id).ToArrayAsync();
    }
    
    public async Task<long[]> GetUnreadPlanets(long userId)
    {
        var query =
            from c in _db.Channels.Where(x => x.PlanetId != null)
            join s in _db.UserChannelStates.Where(x => x.UserId == userId)
                on c.Id equals s.ChannelId
                into grouping
            from g in grouping.DefaultIfEmpty()
            where g == null || c.LastUpdateTime > g.LastViewedTime
            select c;
        
        return await query
            .Select(x => x.PlanetId.Value)
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