using System.Collections.Concurrent;
using Valour.Shared.Models;
using ChannelState = Valour.Database.ChannelState;

namespace Valour.Server.Services;

public class ChannelStateService
{
    private readonly ValourDB _db;
    private readonly ILogger<ChannelStateService> _logger;

    private static ConcurrentDictionary<long, ChannelState> _channelStateCache = new();

    public ChannelStateService(ValourDB db, ILogger<ChannelStateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<ChannelState> GenerateState(long channelId)
    {
        // Use last posted message time as last update time
        var stateData = await _db.Channels
            .Where(x => x.Id == channelId)
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                Time = x.Messages.OrderByDescending(x => x.Id).Select(x => x.TimeSent).FirstOrDefault(),
                PlanetId = x.PlanetId
            })
            .FirstOrDefaultAsync();

        return new ChannelState()
        {
            PlanetId = stateData.PlanetId,
            ChannelId = channelId,
            LastUpdateTime = stateData.Time
        };
    }
    
    /// <summary>
    /// Sets the state of a channel
    /// </summary>
    public void SetChannelStateTime(long channelId, DateTime updateTime, long? planetId = null)
    {
        if (_channelStateCache.TryGetValue(channelId, out var state))
        {
            state.LastUpdateTime = updateTime;
        }
        else
        {
            _channelStateCache[channelId] = new ChannelState
            {
                ChannelId = channelId,
                LastUpdateTime = updateTime,
                PlanetId = planetId
            };
        }
    }

    public async Task<ChannelState> GetChannelState(long channelId)
    {
        // Try to get state from cache
        _channelStateCache.TryGetValue(channelId, out var state);
        
        // If not in cache, get from DB
        if (state is null)
        {
            state = await GenerateState(channelId);
            
            // Add to cache
            _channelStateCache[channelId] = state;
        }

        return state;
    }

    /// <summary>
    /// Returns a dict of channel ids to their state
    /// </summary>
    public async Task<Dictionary<long, ChannelState>> GetChannelStates(long userId)
    {
        Dictionary<long, ChannelState> states = new();
        
        // Planet channels
        // We only really need chat channels
        var planetChannels = await _db.MemberChannelAccess.Where(x => 
                x.UserId == userId &&
                x.Channel.ChannelType == ChannelTypeEnum.PlanetChat)
            .Select(x => x.ChannelId)
            .ToListAsync();
        
        // Direct channels
        var directChannels = await _db.ChannelMembers.Where(x => x.UserId == userId)
            .Select(x => x.ChannelId)
            .ToListAsync();
        
        foreach (var pChannelId in planetChannels)
        {
            states[pChannelId] = await GetChannelState(pChannelId);
        }
        
        foreach (var dChannelId in directChannels)
        {
            states[dChannelId] = await GetChannelState(dChannelId);
        }
        
        return states;
    }
}