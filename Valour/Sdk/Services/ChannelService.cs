using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Sdk.Requests;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class ChannelService : ServiceBase
{
    /// <summary>
    /// Run when SignalR opens a channel
    /// </summary>
    public HybridEvent<Channel> ChannelConnected;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public HybridEvent<Channel> ChannelDisconnected;
    
    /// <summary>
    /// Run when a category is reordered
    /// </summary>
    public HybridEvent<CategoryOrderEvent> CategoryReordered;

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

    /// <summary>
    /// The direct chat channels (dms) of this user
    /// </summary>
    public readonly IReadOnlyList<Channel> DirectChatChannels;
    private readonly List<Channel> _directChatChannels = new();

    /// <summary>
    /// Lookup for direct chat channels
    /// </summary>
    public readonly IReadOnlyDictionary<long, Channel> DirectChatChannelsLookup;
    private readonly Dictionary<long, Channel> _directChatChannelsLookup = new();
    
    private readonly LogOptions _logOptions = new(
        "ChannelService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    
    public ChannelService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
        
        ConnectedPlanetChannels = _connectedPlanetChannels;
        ChannelLocks = _channelLocks;
        ConnectedPlanetChannelsLookup = _connectedPlanetChannelsLookup;
        
        DirectChatChannels = _directChatChannels;
        DirectChatChannelsLookup = _directChatChannelsLookup;
        
        SetupLogging(client.Logger, _logOptions);
        
        // Reconnect channels on node reconnect
        client.NodeService.NodeReconnected += OnNodeReconnect;
        client.NodeService.NodeAdded += HookHubEvents;
    }
    
    /// <summary>
    /// Given a channel id, returns the channel. Planet channels should be fetched via the Planet.
    /// </summary>
    public async ValueTask<Channel> FetchDirectChannelAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _cache.Channels.TryGet(id, out var cached))
            return cached;

        var channel = (await _client.PrimaryNode.GetJsonAsync<Channel>(ISharedChannel.GetDirectIdRoute(id))).Data;

        return _cache.Sync(channel);
    }

    public async ValueTask<Channel> FetchPlanetChannelAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchPlanetChannelAsync(id, planet, skipCache);
    }
    
    public async ValueTask<Channel> FetchPlanetChannelAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _cache.Channels.TryGet(id, out var cached))
            return cached;

        var channel = (await planet.Node.GetJsonAsync<Channel>(ISharedChannel.GetPlanetIdRoute(planet.Id, id))).Data;

        return _cache.Sync(channel);
    }

    public Task<TaskResult<Channel>> CreatePlanetChannelAsync(Planet planet, CreateChannelRequest request)
    {
        request.Channel.PlanetId = planet.Id;
        return planet.Node.PostAsyncWithResponse<Channel>(request.Channel.BaseRoute, request);
    }
    
    /// <summary>
    /// Given a user id, returns the direct channel between them and the requester.
    /// If create is true, this will create the channel if it is not found.
    /// </summary>
    public async ValueTask<Channel> FetchDmChannelAsync(long otherUserId, bool create = false, bool skipCache = false)
    {
        var key = new DirectChannelKey(_client.Me.Id, otherUserId);

        if (!skipCache &&
            _cache.DmChannelKeyToId.TryGetValue(key, out var id) &&
            _cache.Channels.TryGet(id, out var cached))
            return cached;

        var dmChannel = (await _client.PrimaryNode.GetJsonAsync<Channel>(
            $"{ISharedChannel.DirectBaseRoute}/byUser/{otherUserId}?create={create}")).Data;

        return _cache.Sync(dmChannel);
    }
    
    public async Task LoadDmChannelsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Channel>>("api/channels/direct/self");
        if (!response.Success)
        {
            LogError("Failed to load direct chat channels!");
            LogError(response.Message);

            return;
        }
        
        // Clear existing
        _directChatChannels.Clear();
        _directChatChannelsLookup.Clear();
        
        foreach (var channel in response.Data)
        {
            // Custom cache insert behavior
            if (channel.Members is not null && channel.Members.Count > 0)
            {
                var id0 = channel.Members[0].Id;
                
                // Self channel
                if (channel.Members.Count == 1)
                {
                    var key = new DirectChannelKey(id0, id0);
                    _cache.DmChannelKeyToId.Add(key, channel.Id);
                }
                // Other channel
                else if (channel.Members.Count == 2)
                {
                    var id1 = channel.Members[1].Id;
                    var key = new DirectChannelKey(id0, id1);
                    _cache.DmChannelKeyToId.Add(key, channel.Id);
                }
            }

            var cached = _cache.Sync(channel);
            _directChatChannels.Add(cached);
            _directChatChannelsLookup.Add(cached.Id, cached);
        }
        
        Log($"Loaded {DirectChatChannels.Count} direct chat channels...");
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
        
        if (!result.Success)
        {
            LogError(result.Message);
            return result;
        }
        
        Log(result.Message);
        
        // Add to open set
        _connectedPlanetChannels.Add(channel);
        _connectedPlanetChannelsLookup[channel.Id] = channel;

        Log($"Joined SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelConnected?.Invoke(channel);
        
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

        Log($"Left SignalR group for channel {channel.Name} ({channel.Id})");

        ChannelDisconnected?.Invoke(channel);

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
            Log($"Channel lock {key} removed.");
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

    
    // TODO: change
    public void OnCategoryOrderUpdate(CategoryOrderEvent eventData)
    {
        // Update channels in cache
        uint pos = 0;
        foreach (var data in eventData.Order)
        {
            if (_client.Cache.Channels.TryGet(data.Id, out var channel))
            {
                // The parent can be changed in this event
                channel.ParentId = eventData.CategoryId;

                // Position can be changed in this event
                channel.RawPosition = pos;
            }

            pos++;
        }
        
        CategoryReordered?.Invoke(eventData);
    }
    
    public void OnWatchingUpdate(ChannelWatchingUpdate update)
    {
        if (!_cache.Channels.TryGet(update.ChannelId, out var channel))
            return;
        
        channel.WatchingUpdated?.Invoke(update);
    }

    public void OnTypingUpdate(ChannelTypingUpdate update)
    {
        if (!_cache.Channels.TryGet(update.ChannelId, out var channel))
            return;
        
        channel.TypingUpdated?.Invoke(update);
    }

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<CategoryOrderEvent>("CategoryOrder-Update", OnCategoryOrderUpdate);
        node.HubConnection.On<ChannelWatchingUpdate>("Channel-Watching-Update", OnWatchingUpdate);
        node.HubConnection.On<ChannelTypingUpdate>("Channel-CurrentlyTyping-Update", OnTypingUpdate);
    }
    
    private async Task OnNodeReconnect(Node node)
    {
        foreach (var channel in _connectedPlanetChannels.Where(x => x.Node?.Name == node.Name))
        {
            await node.HubConnection.SendAsync("JoinChannel", channel.Id);
            Log($"Rejoined SignalR group for channel {channel.Id}");
        }
    }
}