using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class PlanetChannelService
{
    /// <summary>
    /// Run when SignalR opens a channel
    /// </summary>
    public static HybridEvent<Channel> ChannelOpened;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public static HybridEvent<Channel> ChannelClosed;
    
    /// <summary>
    /// Currently opened channels
    /// </summary>
    public static IReadOnlyList<Channel> ConnectedPlanetChannels { get; private set; }
    private static readonly List<Channel> ConnectedPlanetChannelsInternal = new();
    
    /// <summary>
    /// Connected channels lookup
    /// </summary>
    public static IReadOnlyDictionary<long, Channel> ConnectedPlanetChannelsLookup { get; private set; }
    private static readonly Dictionary<long, Channel> ConnectedPlanetChannelsLookupInternal = new();

    /// <summary>
    /// A set of locks used to prevent channel connections from closing automatically
    /// </summary>
    public static IReadOnlyDictionary<string, long> ChannelLocks { get; private set; }
    private static readonly Dictionary<string, long> ChannelLocksInternal = new();
    
    static PlanetChannelService()
    {
        ConnectedPlanetChannels = ConnectedPlanetChannelsInternal;
        ChannelLocks = ChannelLocksInternal;
        ConnectedPlanetChannelsLookup = ConnectedPlanetChannelsLookupInternal;
        
        // Reconnect channels on node reconnect
        NodeService.NodeReconnected += OnNodeReconnect;
    }
    
    /// <summary>
    /// Opens a SignalR connection to a channel if it does not already have one,
    /// and stores a key to prevent it from being closed
    /// </summary>
    public static async Task<TaskResult> TryOpenPlanetChannelConnection(Channel channel, string key)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Channel is not a planet chat channel");

        if (ChannelLocksInternal.ContainsKey(key))
        {
            ChannelLocksInternal[key] = channel.Id;
        }
        else
        {
            // Add lock
            AddChannelLock(key, channel.Id);   
        }
        
        // Already opened
        if (ConnectedPlanetChannelsInternal.Contains(channel))
            return TaskResult.SuccessResult;

        var planet = channel.Planet;

        // Ensure planet is opened
        var planetResult = await PlanetService.TryOpenPlanetConnection(planet, key);
        if (!planetResult.Success)
            return planetResult;

        // Join channel SignalR group
        var result = await channel.Node.HubConnection.InvokeAsync<TaskResult>("JoinChannel", channel.Id);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return result;

        // Add to open set
        ConnectedPlanetChannelsInternal.Add(channel);
        ConnectedPlanetChannelsLookupInternal[channel.Id] = channel;

        Console.WriteLine($"Joined SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelOpened?.Invoke(channel);
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Closes a SignalR connection to a channel
    /// </summary>
    public static async Task<TaskResult> TryClosePlanetChannelConnection(Channel channel, string key, bool force = false)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Channel is not a planet chat channel");

        if (!force)
        {
            // Remove key from locks
            var lockResult = RemoveChannelLock(key);

            // If there are still any locks, don't close
            if (lockResult == ConnectionLockResult.Locked)
            {
                return TaskResult.FromFailure("Channel is locked by other keys.");
            } 
            // If for some reason our key isn't actually there
            // (shouldn't happen, but just in case)
            else if (lockResult == ConnectionLockResult.NotFound)
            {
                if (ChannelLocksInternal.Values.Any(x => x == channel.Id))
                {
                    return TaskResult.FromFailure("Channel is locked by other keys.");
                }
            }
        }

        // Not opened
        if (!ConnectedPlanetChannelsInternal.Contains(channel))
            return TaskResult.FromFailure("Channel is not open.");

        // Leaves channel SignalR group
        await channel.Node.HubConnection.SendAsync("LeaveChannel", channel.Id);

        // Remove from open set
        ConnectedPlanetChannelsInternal.Remove(channel);
        ConnectedPlanetChannelsLookupInternal.Remove(channel.Id);

        Console.WriteLine($"Left SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelClosed?.Invoke(channel);

        // Close planet connection if no other channels are open
        await PlanetService.TryClosePlanetConnection(channel.Planet, key);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Prevents a channel from closing connections automatically.
    /// Key is used to allow multiple locks per channel.
    /// </summary>
    private static void AddChannelLock(string key, long planetId)
    {
        ChannelLocksInternal[key] = planetId;
    }

    /// <summary>
    /// Removes the lock for a channel.
    /// Returns the result of if there are any locks left for the channel.
    /// </summary>
    private static ConnectionLockResult RemoveChannelLock(string key)
    {
        if (ChannelLocksInternal.TryGetValue(key, out var channelId))
        {
            Console.WriteLine($"Channel lock {key} removed.");
            ChannelLocksInternal.Remove(key);
            return ChannelLocksInternal.Any(x => x.Value == channelId)
                ? ConnectionLockResult.Locked
                : ConnectionLockResult.Unlocked;
        }
        
        return ConnectionLockResult.NotFound;
    }
    
    /// <summary>
    /// Returns if the channel is open
    /// </summary>
    public static bool IsChannelConnected(long channelId) =>
        ConnectedPlanetChannelsLookupInternal.ContainsKey(channelId);

    public static async Task OnNodeReconnect(Node node)
    {
        foreach (var channel in ConnectedPlanetChannelsInternal.Where(x => x.Node?.Name == node.Name))
        {
            await node.HubConnection.SendAsync("JoinChannel", channel.Id);
            await node.Log($"Rejoined SignalR group for channel {channel.Id}", "lime");
        }
    }
}