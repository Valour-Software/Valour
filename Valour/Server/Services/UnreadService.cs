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
            .Where(c => c.PlanetId == planetId && c.ChannelType != ChannelTypeEnum.PlanetCategory)
            .Where(c => !_db.UserChannelStates
                .Where(s => s.UserId == userId && s.PlanetId == planetId)
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
            .GroupJoin(
                _db.UserChannelStates.Where(s => s.UserId == userId),
                channel => channel.Id,
                state => state.ChannelId,
                (channel, states) => new { channel, states }
            )
            .SelectMany(
                x => x.states.DefaultIfEmpty(),
                (x, state) => new { x.channel, state }
            )
            .Where(x => x.state == null || x.state.LastViewedTime < x.channel.LastUpdateTime)
            .Select(x => x.channel.PlanetId.Value)
            .Distinct()
            .ToArrayAsync();
    }
    
    public async Task<UserChannelState> UpdateReadState(long channelId, long userId, long? planetId, long? memberId, DateTime? updateTime)
    {
        updateTime ??= DateTime.UtcNow;

        var channelState = await _db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId);

        if (channelState is null)
        {
            channelState = new UserChannelState()
            {
                UserId = userId,
                ChannelId = channelId,
                PlanetId = planetId,
                PlanetMemberId = memberId,
                LastViewedTime = updateTime.Value
            }.ToDatabase();

            _db.UserChannelStates.Add(channelState);
        }
        else
        {
            channelState.LastViewedTime = updateTime.Value;
        }
        
        await _db.SaveChangesAsync();

        return channelState.ToModel();
    }
}