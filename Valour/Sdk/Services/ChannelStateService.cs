using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class ChannelStateService : ServiceBase
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
    public readonly IReadOnlyDictionary<long, DateTime?> ChannelsLastViewedState;
    private readonly Dictionary<long, DateTime?> _channelsLastViewedState = new();

    /// <summary>
    /// The last update times of channels this user has access to
    /// </summary>
    public readonly IReadOnlyDictionary<long, ChannelState> CurrentChannelStates;
    private readonly Dictionary<long, ChannelState> _currentChannelStates = new();
    
    private static readonly LogOptions LogOptions = new(
        "ChannelStateService",
        "#5c33a3",
        "#a33340",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    
    public ChannelStateService(ValourClient client)
    {
        _client = client;

        ChannelsLastViewedState = _channelsLastViewedState;
        CurrentChannelStates = _currentChannelStates;
        
        SetupLogging(client.Logger, LogOptions);
        
        _client.NodeService.NodeAdded += HookHubEvents;
    }
    
    public async Task LoadChannelStatesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<ChannelStateData>>($"api/users/me/statedata");
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
        if (_client.ChannelService.IsChannelConnected(channelId))
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
        if (!_client.Cache.Channels.TryGet(update.ChannelId, out var channel))
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

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<ChannelStateUpdate>("Channel-State", OnChannelStateUpdated);
        node.HubConnection.On<UserChannelState>("UserChannelState-Update", OnUserChannelStateUpdated);
    }
}