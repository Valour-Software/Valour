using System.Net;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
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
    /// Currently opened channels
    /// </summary>
    public readonly IReadOnlyList<Channel> ConnectedPlanetChannels;
    private readonly List<Channel> _connectedPlanetChannels = new();
    private readonly object _connectedPlanetChannelsLock = new();

    /// <summary>
    /// Connected channels lookup
    /// </summary>
    public readonly IReadOnlyDictionary<long, Channel> ConnectedPlanetChannelsLookup;
    private readonly ConcurrentDictionary<long, Channel> _connectedPlanetChannelsLookup = new();

    /// <summary>
    /// A set of locks used to prevent channel connections from closing automatically
    /// </summary>
    public readonly IReadOnlyDictionary<string, long> ChannelLocks;
    private readonly ConcurrentDictionary<string, long> _channelLocks = new();

    /// <summary>
    /// Serializes open and close operations for each channel while allowing
    /// unrelated channels to initialize concurrently.
    /// </summary>
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _channelConnectionGates = new();

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

        var response = await _client.PrimaryNode.GetJsonAsync<Channel>(ISharedChannel.GetDirectIdRoute(id));
        if (!response.Success || response.Data is null)
        {
            LogError($"Failed to fetch direct channel {id}: {response.Message}");
            return null;
        }

        return response.Data.Sync(_client);
    }

    public async ValueTask<Channel> FetchPlanetChannelAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
        {
            LogError($"Failed to fetch planet channel {id}: could not load planet {planetId}.");
            return null;
        }

        return await FetchPlanetChannelAsync(id, planet, skipCache);
    }
    
    public async ValueTask<Channel> FetchPlanetChannelAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _cache.Channels.TryGet(id, out var cached))
            return cached;

        if (planet?.Node is null)
        {
            LogError($"Failed to fetch planet channel {id}: planet node is unavailable.");
            return null;
        }

        var response = await planet.Node.GetJsonAsync<Channel>(ISharedChannel.GetPlanetIdRoute(planet.Id, id));
        if (!response.Success || response.Data is null)
        {
            LogError($"Failed to fetch planet channel {id} in planet {planet.Id}: {response.Message}");
            return null;
        }

        return response.Data.Sync(_client);
    }

    public async Task<TaskResult<Channel>> CreatePlanetChannelAsync(Planet planet, CreateChannelRequest request)
    {
        request.Channel.PlanetId = planet.Id;
        var result = await planet.Node.PostAsyncWithResponse<Channel>(request.Channel.BaseRoute, request);
        if (!result.Success || result.Data is null)
            return result;

        // Do not depend on the realtime notification racing the HTTP response.
        // The creating client may not have joined the planet group yet (notably
        // immediately after creating a planet), and would otherwise keep a
        // stale channel tree until the next full fetch or reload.
        var cached = result.Data.Sync(_client);
        return new TaskResult<Channel>(
            true,
            result.Message,
            cached,
            result.Details,
            result.Code);
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

        var response = await _client.PrimaryNode.GetJsonAsync<Channel>(
            $"{ISharedChannel.DirectBaseRoute}/byUser/{otherUserId}?create={create}");
        if (!response.Success || response.Data is null)
        {
            LogError($"Failed to fetch DM channel with user {otherUserId}: {response.Message}");
            return null;
        }

        return response.Data.Sync(_client);
    }
    
    public async Task<List<PlanetMember>> FetchRecentChattersAsync(Channel channel)
    {
        return await FetchRecentChattersAsync(channel.Id, channel.Planet);
    }

    public async Task<List<PlanetMember>> FetchRecentChattersAsync(long channelId, Planet planet)
    {
        var result = await planet.Node.GetJsonAsync<List<PlanetMember>>($"{ISharedChannel.GetPlanetIdRoute(planet.Id, channelId)}/recentChatters");
        if (!result.Success || result.Data is null)
        {
            LogError($"Failed to fetch recent chatters for channel {channelId}: {result.Message}");
            return new List<PlanetMember>();
        }

        result.Data.SyncAll(_client);
        return result.Data;
    }

    public async Task<TaskResult> MoveChannelAsync(Channel toMove, Channel? destination, bool placeBefore, bool insideCategory = false)
    {
        if (toMove.PlanetId is null)
        {
            LogError("Trying to move channel without a planet id!");
            return TaskResult.FromFailure("Channel does not have a planet id!");
        }

        var request = new MoveChannelRequest()
        {
            PlanetId = toMove.PlanetId!.Value,
            DestinationChannel = destination?.Id,
            MovingChannel = toMove.Id,
            PlaceBefore = placeBefore,
            InsideCategory = insideCategory
        };

        var result = await toMove.Node.PostAsync($"api/planets/{toMove.PlanetId}/channels/move", request);

        if (result.Success)
        {
            // The move endpoint has no response body. Refresh the authoritative
            // tree instead of relying on the Channels-Moved hub event racing
            // the HTTP response; newly created planets may still be joining
            // their realtime group when their first channels are organized.
            await toMove.Planet.FetchChannelsAsync();
        }

        return result;
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

        ApplyDirectChannels(response.Data);
    }

    public void ApplyDirectChannels(IEnumerable<Channel> channels)
    {
        
        // Clear existing
        _directChatChannels.Clear();
        _directChatChannelsLookup.Clear();
        _cache.DmChannelKeyToId.Clear();

        foreach (var channel in channels ?? [])
            RegisterDirectChatChannel(channel);
        
        Log($"Loaded {DirectChatChannels.Count} direct chat channels...");
    }

    public async Task<QueryResponse<DirectMessageListItem>> QueryDirectMessageListAsync(
        int skip = 0,
        int take = 50,
        string search = null)
    {
        var route = $"api/channels/direct/self/query?skip={skip}&take={take}";

        if (!string.IsNullOrWhiteSpace(search))
            route += $"&search={WebUtility.UrlEncode(search)}";

        var response = await _client.PrimaryNode.GetJsonAsync<QueryResponse<DirectMessageListItem>>(route);
        if (!response.Success || response.Data is null)
        {
            LogError($"Failed to query direct messages: {response.Message}");
            return QueryResponse<DirectMessageListItem>.Empty;
        }

        response.Data.Items ??= new List<DirectMessageListItem>();

        foreach (var item in response.Data.Items)
        {
            item.Sync(_client);
            RegisterDirectChatChannel(item.Channel);
        }

        return response.Data;
    }

    private void RegisterDirectChatChannel(Channel channel)
    {
        if (channel is null)
            return;

        if (channel.Members is not null && channel.Members.Count > 0)
        {
            var userId0 = channel.Members[0].UserId;

            if (channel.Members.Count == 1)
            {
                var key = new DirectChannelKey(userId0, userId0);
                _cache.DmChannelKeyToId[key] = channel.Id;
            }
            else if (channel.Members.Count == 2)
            {
                var userId1 = channel.Members[1].UserId;
                var key = new DirectChannelKey(userId0, userId1);
                _cache.DmChannelKeyToId[key] = channel.Id;
            }
        }

        var cached = channel.Sync(_client);

        if (_directChatChannelsLookup.ContainsKey(cached.Id))
        {
            _directChatChannelsLookup[cached.Id] = cached;
            return;
        }

        _directChatChannels.Add(cached);
        _directChatChannelsLookup[cached.Id] = cached;
    }
    
    /// <summary>
    /// Opens a SignalR connection to a channel if it does not already have one,
    /// and stores a key to prevent it from being closed
    /// </summary>
    public async Task<TaskResult> TryOpenPlanetChannelConnection(Channel channel, string key)
    {
        if (channel is null)
            return TaskResult.FromFailure("Channel is null");

        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Channel is not a planet chat channel");

        AddChannelLock(key, channel.Id);

        var connectionGate = _channelConnectionGates.GetOrAdd(
            channel.Id,
            static _ => new SemaphoreSlim(1, 1));

        await connectionGate.WaitAsync();
        try
        {
            var planet = channel.Planet;
            var planetResult = await _client.PlanetService.TryOpenPlanetConnection(planet, key);
            if (!planetResult.Success)
            {
                RemoveChannelLock(key, channel.Id);
                return planetResult;
            }

            // Every channel owner also needs its own planet lock, including
            // callers joining a channel that another component already opened.
            if (_connectedPlanetChannelsLookup.ContainsKey(channel.Id))
                return TaskResult.SuccessResult;

            try
            {
                _ = await FetchRecentChattersAsync(channel);

                var result = await channel.ConnectToRealtime();
                if (!result.Success)
                {
                    LogError(result.Message);
                    RemoveChannelLock(key, channel.Id);
                    await _client.PlanetService.TryClosePlanetConnection(planet, key);
                    return result;
                }

                Log(result.Message);

                lock (_connectedPlanetChannelsLock)
                {
                    if (_connectedPlanetChannels.All(x => x.Id != channel.Id))
                        _connectedPlanetChannels.Add(channel);
                }

                _connectedPlanetChannelsLookup[channel.Id] = channel;

                Log($"Joined SignalR group for channel {channel.Name} ({channel.Id})");
                ChannelConnected?.Invoke(channel);

                return TaskResult.SuccessResult;
            }
            catch (Exception ex)
            {
                LogError($"Unexpected exception opening channel {channel.Id}: {ex.Message}");
                RemoveChannelLock(key, channel.Id);
                await _client.PlanetService.TryClosePlanetConnection(planet, key);
                return TaskResult.FromFailure("Unexpected exception opening channel connection.");
            }
        }
        finally
        {
            connectionGate.Release();
        }
    }

    /// <summary>
    /// Closes a SignalR connection to a channel
    /// </summary>
    public async Task<TaskResult> TryClosePlanetChannelConnection(Channel channel, string key, bool force = false)
    {
        if (channel is null)
            return TaskResult.FromFailure("Channel is null");

        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Channel is not a planet chat channel");

        if (!force)
        {
            // Remove key from locks
            var lockResult = RemoveChannelLock(key, channel.Id);

            // If there are still any locks, don't close
            if (lockResult == ConnectionLockResult.Locked)
            {
                // This owner no longer needs the planet even though another
                // owner is keeping the shared channel alive.
                await _client.PlanetService.TryClosePlanetConnection(channel.Planet, key);
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

        var connectionGate = _channelConnectionGates.GetOrAdd(
            channel.Id,
            static _ => new SemaphoreSlim(1, 1));

        await connectionGate.WaitAsync();
        try
        {
            if (!force && _channelLocks.Values.Any(x => x == channel.Id))
            {
                await _client.PlanetService.TryClosePlanetConnection(channel.Planet, key);
                return TaskResult.FromFailure("Channel is locked by other keys.");
            }

            if (!_connectedPlanetChannelsLookup.TryGetValue(channel.Id, out var connectedChannel))
            {
                await _client.PlanetService.TryClosePlanetConnection(channel.Planet, key);
                return TaskResult.SuccessResult;
            }

            await connectedChannel.DisconnectFromRealtime();

            lock (_connectedPlanetChannelsLock)
                _connectedPlanetChannels.RemoveAll(x => x.Id == channel.Id);

            _connectedPlanetChannelsLookup.TryRemove(channel.Id, out _);

            Log($"Left SignalR group for channel {connectedChannel.Name} ({channel.Id})");
            ChannelDisconnected?.Invoke(connectedChannel);

            await _client.PlanetService.TryClosePlanetConnection(connectedChannel.Planet, key);
            return TaskResult.SuccessResult;
        }
        finally
        {
            connectionGate.Release();
        }
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
    private ConnectionLockResult RemoveChannelLock(string key, long expectedChannelId)
    {
        var entry = new KeyValuePair<string, long>(key, expectedChannelId);
        if (((ICollection<KeyValuePair<string, long>>)_channelLocks).Remove(entry))
        {
            Log($"Channel lock {key} removed.");
            return _channelLocks.Values.Any(x => x == expectedChannelId)
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
    
    public void OnWatchingUpdate(ChannelWatchingUpdate update)
    {
        if (update.PlanetId is not null)
        {
            // Get channel from planet
            if (!_cache.Planets.TryGet(update.PlanetId.Value, out var planet))
                return;
            
            if (!planet!.Channels.TryGet(update.ChannelId, out var channel))
                return;
            
            channel?.WatchingUpdated?.Invoke(update);
        }
        else
        {
            if (!_cache.Channels.TryGet(update.ChannelId, out var channel))
                return;
            
            channel?.WatchingUpdated?.Invoke(update);
        }
    }

    public void OnTypingUpdate(ChannelTypingUpdate update)
    {
        if (update.PlanetId is not null)
        {
            // Get channel from planet
            if (!_cache.Planets.TryGet(update.PlanetId.Value, out var planet))
                return;
            
            if (!planet!.Channels.TryGet(update.ChannelId, out var channel))
                return;
            
            channel?.TypingUpdated?.Invoke(update);
        }
        else
        {
            if (!_cache.Channels.TryGet(update.ChannelId, out var channel))
                return;
            
            channel?.TypingUpdated?.Invoke(update);
        }
    }
    
    public void OnChannelsMoved(ChannelsMovedEvent e)
    {
        var planet = _client.Cache.Planets.Get(e.PlanetId);
        if (planet is null)
            return;

        planet.OnChannelsMoved(e);
    }

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<ChannelsMovedEvent>("Channels-Moved", update =>
        {
            if (node.AcceptsExternalPlanetRealtimeEvent(update?.PlanetId))
                OnChannelsMoved(update);
        });
        node.HubConnection.On<ChannelWatchingUpdate>("Channel-Watching-Update", update =>
        {
            if (node.AcceptsExternalPlanetRealtimeEvent(update?.PlanetId))
                OnWatchingUpdate(update);
        });
        node.HubConnection.On<ChannelTypingUpdate>("Channel-CurrentlyTyping-Update", update =>
        {
            if (node.AcceptsExternalPlanetRealtimeEvent(update?.PlanetId))
                OnTypingUpdate(update);
        });
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
