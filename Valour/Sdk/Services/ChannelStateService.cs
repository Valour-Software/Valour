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

        SetupLogging(client.Logger, LogOptions);
        
        _client.NodeService.NodeAdded += HookHubEvents;
    }
    
    public void OnChannelStateUpdated(ChannelStateUpdate update)
    {
        // Right now only planet chat channels have state updates
        Channel channel = null;
        if (update.PlanetId.HasValue &&
            _client.Cache.Planets.TryGet(update.PlanetId.Value, out var planet))
        {
            planet!.Channels.TryGet(update.ChannelId, out channel);
        }
        else
        {
            _client.Cache.Channels.TryGet(update.ChannelId, out channel);
        }

        if (channel is not null)
        {
            channel.LastUpdateTime = update.Time;
        }
        
        ChannelStateUpdated?.Invoke(update);
    }
    
    public void OnUserChannelStateUpdated(UserChannelState channelState)
    {
        UserChannelStateUpdated?.Invoke(channelState);
    }

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<ChannelStateUpdate>("Channel-State", update =>
        {
            if (node.AcceptsExternalPlanetRealtimeEvent(update.PlanetId))
                OnChannelStateUpdated(update);
        });

        // User channel state has no planet provenance, so it is deliberately
        // hub-only. An external node cannot update direct-message read state.
        if (!node.IsExternal)
            node.HubConnection.On<UserChannelState>("UserChannelState-Update", OnUserChannelStateUpdated);
    }
}
