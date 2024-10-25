using Valour.Sdk.Client;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class ChannelStateService
{
    /// <summary>
    /// Run when a UserChannelState is updated
    /// </summary>
    public HybridEvent<UserChannelState> UserChannelStateUpdated;
    
    /// <summary>
    /// Run when a channel state updates
    /// </summary>
    public HybridEvent<ChannelStateUpdate> ChannelStateUpdated;
    
    /// <summary>
    /// The state of channels this user has access to
    /// </summary>
    public IReadOnlyDictionary<long, DateTime?> ChannelsLastViewedState { get; private set; }
    private readonly Dictionary<long, DateTime?> _channelsLastViewedState = new();
    
    /// <summary>
    /// The last update times of channels this user has access to
    /// </summary>
    public IReadOnlyDictionary<long, ChannelState> CurrentChannelStates { get; private set; }
    private readonly Dictionary<long, ChannelState> _currentChannelStates = new();
    
    private readonly ValourClient _client;
    
    public ChannelStateService(ValourClient client)
    {
        _client = client;

        ChannelsLastViewedState = _channelsLastViewedState;
        CurrentChannelStates = _currentChannelStates;
    }
    
    public async Task LoadChannelStatesAsync()
    {
        var response = await _client.GetJsonAsync<List<ChannelStateData>>($"api/users/self/statedata");
        if (!response.Success)
        {
            Console.WriteLine("** Failed to load channel states **");
            Console.WriteLine(response.Message);

            return;
        }

        foreach (var state in response.Data)
        {
            if (state.ChannelState is not null)
                _currentChannelStates[state.ChannelId] = state.ChannelState;
            
            if (state.LastViewedTime is not null)
                _channelsLastViewedState[state.ChannelId] = state.LastViewedTime;
        }

        Console.WriteLine("Loaded " + ChannelsLastViewedState.Count + " channel states.");
        // Console.WriteLine(JsonSerializer.Serialize(response.Data));
    }
    
    public void SetChannelLastViewedState(long channelId, DateTime lastViewed)
    {
        _channelsLastViewedState[channelId] = lastViewed;
    }

    public bool GetPlanetUnreadState(long planetId)
    {
        var channelStates = 
            CurrentChannelStates.Where(x => x.Value.PlanetId == planetId);
        
        foreach (var state in channelStates)
        {
            if (GetChannelUnreadState(state.Key))
                return true;
        }

        return false;
    }

    public bool GetChannelUnreadState(long channelId)
    {
        // TODO: this will act weird with multiple tabs
        if (_client.PlanetChannelService.IsChannelConnected(channelId))
            return false;

        if (!_channelsLastViewedState.TryGetValue(channelId, out var lastRead))
        {
            return true;
        }

        if (!_currentChannelStates.TryGetValue(channelId, out var lastUpdate))
        {
            return false;
        }

        return lastRead < lastUpdate.LastUpdateTime;
    }
    
    public void OnChannelStateUpdated(ChannelStateUpdate update)
    {
        // Right now only planet chat channels have state updates
        if (!Channel.Cache.TryGet(update.ChannelId, out var channel))
        {
            return;
        }
        
        if (!_currentChannelStates.TryGetValue(channel.Id, out var state))
        {
            state = new ChannelState()
            {
                ChannelId = update.ChannelId,
                PlanetId = update.PlanetId,
                LastUpdateTime = update.Time
            };

            _currentChannelStates[channel.Id] = state;
        }
        else
        {
            _currentChannelStates[channel.Id].LastUpdateTime = update.Time;
        }
        
        ChannelStateUpdated?.Invoke(update);
    }
    
    public void OnUserChannelStateUpdated(UserChannelState channelState)
    {
        _channelsLastViewedState[channelState.ChannelId] = channelState.LastViewedTime;
        UserChannelStateUpdated?.Invoke(channelState);
    }
}