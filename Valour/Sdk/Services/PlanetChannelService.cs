using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class PlanetChannelService : ServiceBase
{
    /// <summary>
    /// Run when SignalR opens a channel
    /// </summary>
    public HybridEvent<Channel> ChannelOpened;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public HybridEvent<Channel> ChannelClosed;

    /// <summary>
    /// Currently opened channels
    /// </summary>
    public readonly IReadOnlyList<Channel> ConnectedPlanetChannels;
    private readonly List<Channel> _connectedPlanetChannels = new();

    /// <summary>
    /// Connected channels lookup
    /// </summary>
    public readonly IReadOnlyDictionary<long, Channel> ConnectedPlanetChannelsLookup;
    private readonly Dictionary<long, Channel> _connectedPlanetChannelsLookup = new();

    /// <summary>
    /// A set of locks used to prevent channel connections from closing automatically
    /// </summary>
    public readonly IReadOnlyDictionary<string, long> ChannelLocks;
    private readonly Dictionary<string, long> _channelLocks = new();
    
    private readonly LogOptions _logOptions = new(
        "PlanetChannelService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    
    public PlanetChannelService(ValourClient client)
    {
        _client = client;
        
        ConnectedPlanetChannels = _connectedPlanetChannels;
        ChannelLocks = _channelLocks;
        ConnectedPlanetChannelsLookup = _connectedPlanetChannelsLookup;
        
        SetupLogging(client.Logger, _logOptions);
        
        // Reconnect channels on node reconnect
        client.NodeService.NodeReconnected += OnNodeReconnect;
    }
    
    /// <summary>
    /// Opens a SignalR connection to a channel if it does not already have one,
    /// and stores a key to prevent it from being closed
    /// </summary>
    public async Task<TaskResult> TryOpenPlanetChannelConnection(Channel channel, string key)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Channel is not a planet chat channel");

        if (_channelLocks.ContainsKey(key))
        {
            _channelLocks[key] = channel.Id;
        }
        else
        {
            // Add lock
            AddChannelLock(key, channel.Id);   
        }
        
        // Already opened
        if (_connectedPlanetChannels.Contains(channel))
            return TaskResult.SuccessResult;

        var planet = channel.Planet;

        // Ensure planet is opened
        var planetResult = await _client.PlanetService.TryOpenPlanetConnection(planet, key);
        if (!planetResult.Success)
            return planetResult;

        // Join channel SignalR group
        var result = await channel.Node.HubConnection.InvokeAsync<TaskResult>("JoinChannel", channel.Id);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return result;

        // Add to open set
        _connectedPlanetChannels.Add(channel);
        _connectedPlanetChannelsLookup[channel.Id] = channel;

        Console.WriteLine($"Joined SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelOpened?.Invoke(channel);
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Closes a SignalR connection to a channel
    /// </summary>
    public async Task<TaskResult> TryClosePlanetChannelConnection(Channel channel, string key, bool force = false)
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
                if (_channelLocks.Values.Any(x => x == channel.Id))
                {
                    return TaskResult.FromFailure("Channel is locked by other keys.");
                }
            }
        }

        // Not opened
        if (!_connectedPlanetChannels.Contains(channel))
            return TaskResult.FromFailure("Channel is not open.");

        // Leaves channel SignalR group
        await channel.Node.HubConnection.SendAsync("LeaveChannel", channel.Id);

        // Remove from open set
        _connectedPlanetChannels.Remove(channel);
        _connectedPlanetChannelsLookup.Remove(channel.Id);

        Console.WriteLine($"Left SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelClosed?.Invoke(channel);

        // Close planet connection if no other channels are open
        await _client.PlanetService.TryClosePlanetConnection(channel.Planet, key);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Prevents a channel from closing connections automatically.
    /// Key is used to allow multiple locks per channel.
    /// </summary>
    private void AddChannelLock(string key, long planetId)
    {
        _channelLocks[key] = planetId;
    }

    /// <summary>
    /// Removes the lock for a channel.
    /// Returns the result of if there are any locks left for the channel.
    /// </summary>
    private ConnectionLockResult RemoveChannelLock(string key)
    {
        if (_channelLocks.TryGetValue(key, out var channelId))
        {
            Console.WriteLine($"Channel lock {key} removed.");
            _channelLocks.Remove(key);
            return _channelLocks.Any(x => x.Value == channelId)
                ? ConnectionLockResult.Locked
                : ConnectionLockResult.Unlocked;
        }
        
        return ConnectionLockResult.NotFound;
    }
    
    /// <summary>
    /// Returns if the channel is open
    /// </summary>
    public bool IsChannelConnected(long channelId) =>
        _connectedPlanetChannelsLookup.ContainsKey(channelId);

    private async Task OnNodeReconnect(Node node)
    {
        foreach (var channel in _connectedPlanetChannels.Where(x => x.Node?.Name == node.Name))
        {
            await node.HubConnection.SendAsync("JoinChannel", channel.Id);
            Log($"Rejoined SignalR group for channel {channel.Id}");
        }
    }
}