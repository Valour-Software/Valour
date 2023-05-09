using System.Collections.Concurrent;
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
            state = await _db.ChannelStates.FindAsync(channelId);
            
            // Add to cache
            _channelStateCache[channelId] = state;
        }

        // If still null, return MinValue
        return state;
    }

    /// <summary>
    /// Returns a dict of channel ids to their state
    /// </summary>
    public async Task<Dictionary<long, ChannelState>> GetChannelStates(IEnumerable<long> channelIds)
    {
        Dictionary<long, ChannelState> states = new();
        List<long> missing = new();
        
        foreach (var channelId in channelIds)
        {
            // Try to get state from cache
            _channelStateCache.TryGetValue(channelId, out var state);
            if (state is not null)
            {
                states[channelId] = state;
            }
            else
            {
                missing.Add(channelId);
            }
        }
        
        // Get missing states from DB
        var missingStates = await _db.ChannelStates
            .Where(x => missing.Contains(x.ChannelId))
            .ToListAsync();
        
        // Add missing states to cache and return
        foreach (var state in missingStates)
        {
            states[state.ChannelId] = state;
            _channelStateCache[state.ChannelId] = state;
        }
        
        return states;
    }
}