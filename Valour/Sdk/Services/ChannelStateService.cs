using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class ChannelStateService
{
    /// <summary>
    /// Run when a UserChannelState is updated
    /// </summary>
    public static HybridEvent<UserChannelState> UserChannelStateUpdated;
    
    /// <summary>
    /// Run when a channel state updates
    /// </summary>
    public static HybridEvent<ChannelStateUpdate> ChannelStateUpdated;
    
    /// <summary>
    /// The state of channels this user has access to
    /// </summary>
    public static IReadOnlyDictionary<long, DateTime?> ChannelsLastViewedState { get; private set; }
    private static readonly Dictionary<long, DateTime?> ChannelsLastViewedStateInternal = new();
    
    /// <summary>
    /// The last update times of channels this user has access to
    /// </summary>
    public static IReadOnlyDictionary<long, ChannelState> CurrentChannelStates { get; private set; }
    private static readonly Dictionary<long, ChannelState> CurrentChannelStatesInternal = new();
    
    static ChannelStateService()
    {
        ChannelsLastViewedState = ChannelsLastViewedStateInternal;
        CurrentChannelStates = CurrentChannelStatesInternal;
    }
    
    public static void SetChannelLastViewedState(long channelId, DateTime lastViewed)
    {
        ChannelsLastViewedStateInternal[channelId] = lastViewed;
    }

    public static bool GetPlanetUnreadState(long planetId)
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

    public static bool GetChannelUnreadState(long channelId)
    {
        // TODO: this will act weird with multiple tabs
        if (PlanetChannelService.IsChannelConnected(channelId))
            return false;

        if (!ChannelsLastViewedStateInternal.TryGetValue(channelId, out var lastRead))
        {
            return true;
        }

        if (!CurrentChannelStatesInternal.TryGetValue(channelId, out var lastUpdate))
        {
            return false;
        }

        return lastRead < lastUpdate.LastUpdateTime;
    }
    
    public static void OnChannelStateUpdated(ChannelStateUpdate update)
    {
        // Right now only planet chat channels have state updates
        if (!Channel.Cache.TryGet(update.ChannelId, out var channel))
        {
            return;
        }
        
        if (!CurrentChannelStatesInternal.TryGetValue(channel.Id, out var state))
        {
            state = new ChannelState()
            {
                ChannelId = update.ChannelId,
                PlanetId = update.PlanetId,
                LastUpdateTime = update.Time
            };

            CurrentChannelStatesInternal[channel.Id] = state;
        }
        else
        {
            CurrentChannelStatesInternal[channel.Id].LastUpdateTime = update.Time;
        }
        
        ChannelStateUpdated?.Invoke(update);
    }
    
    public static void HandleUpdateUserChannelState(UserChannelState channelState)
    {
        ChannelsLastViewedStateInternal[channelState.ChannelId] = channelState.LastViewedTime;
        UserChannelStateUpdated?.Invoke(channelState);
    }
}