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
        var effectiveUpdateTime = DateTime.SpecifyKind(updateTime ?? DateTime.UtcNow, DateTimeKind.Utc);

        // Atomic upsert to avoid race conditions when multiple requests update the same
        // (user_id, channel_id) row concurrently.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO user_channel_states (channel_id, user_id, last_viewed_time, planet_id, member_id)
            VALUES ({channelId}, {userId}, {effectiveUpdateTime}, {planetId}, {memberId})
            ON CONFLICT (user_id, channel_id) DO UPDATE
            SET
                last_viewed_time = GREATEST(user_channel_states.last_viewed_time, EXCLUDED.last_viewed_time),
                planet_id = EXCLUDED.planet_id,
                member_id = EXCLUDED.member_id
        ");

        var channelState = await _db.UserChannelStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId);

        if (channelState is null)
        {
            _logger.LogWarning("Failed to load user_channel_state after upsert for user {UserId} channel {ChannelId}", userId, channelId);
            return new UserChannelState
            {
                UserId = userId,
                ChannelId = channelId,
                PlanetId = planetId,
                PlanetMemberId = memberId,
                LastViewedTime = effectiveUpdateTime
            };
        }

        return channelState.ToModel();
    }
}
