#nullable enable

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Hubs;
using Valour.Server.Redis;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Handles node communication and coordination
/// </summary>
public class NodeLifecycleService
{
    // Local node information
    public readonly string Name;
    public readonly string Location;
    public readonly string Version;
    
    private readonly IDatabase _nodeRecords;
    private readonly ILogger<NodeLifecycleService> _logger;
    private readonly ISubscriber _redisChannel;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<CoreHub> _hub;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly IServiceProvider _serviceProvider;
    
    private readonly ModelCacheService _cache;
    
    private readonly string _nodeAliveKey = $"alive:{NodeConfig.Instance.Name}";
    
    private readonly RedisChannel _nodeRelayChannel;
    private readonly RedisChannel _planetRequestChannel;
    private static readonly TimeSpan PlanetClaimLockLifetime = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions RelayJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    

    // Hosted planets //
    
    public NodeLifecycleService(
        IConnectionMultiplexer redis,
        ILogger<NodeLifecycleService> logger,
        IHubContext<CoreHub> hub,
        ModelCacheService cache,
        SignalRConnectionService connectionTracker,
        IServiceProvider serviceProvider)
    {
        _hub = hub;
        _cache = cache;
        _connectionTracker = connectionTracker;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _redis = redis;
        _nodeRecords = redis.GetDatabase(RedisDbTypes.Cluster);
        _redisChannel = redis.GetSubscriber();
        
        _nodeRelayChannel = new RedisChannel($"node-relay-{NodeConfig.Instance.Name}", RedisChannel.PatternMode.Literal);
        _planetRequestChannel = new RedisChannel("planet-requests", RedisChannel.PatternMode.Literal);
        
        var config = NodeConfig.Instance;
        Name = config.Name;
        Location = config.Location;
        Version = typeof(Valour.Shared.Models.ISharedUser).Assembly.GetName().Version!.ToString();
    }
    
    public async Task StartAsync()
    {
        (await _redisChannel.SubscribeAsync(_planetRequestChannel))
            .OnMessage(OnPlanetRequestedAsync);
        
        (await _redisChannel.SubscribeAsync(_nodeRelayChannel))
            .OnMessage(OnNodeRelayEvent);
        
        await AnnounceNode();
    }

    /// <summary>
    /// Announces that a node is live and ready to receive requests
    /// </summary>
    private async Task AnnounceNode()
    {
        _logger.LogInformation("Announcing node {Name} at {Location}", NodeConfig.Instance.Name, NodeConfig.Instance.Location);
        await UpdateNodeAliveAsync();
    }

    /// <summary>
    /// Updates the alive time of a node
    /// </summary>
    public async Task UpdateNodeAliveAsync()
    {
        // Set alive time to utcnow
        await _nodeRecords.StringSetAsync(_nodeAliveKey, DateTime.UtcNow.ToString("O"), flags: CommandFlags.FireAndForget);
    }
    
    /// <summary>
    /// Returns whether a node is alive or not - note that this is not a guarantee that the node is
    /// dead, but rather that it has not updated its alive time in the last 60 seconds
    /// </summary>
    public async Task<bool> IsNodeAliveAsync(string node)
    {
        var key = $"alive:{node}";
        var alive = await _nodeRecords.StringGetAsync(key);
        if (alive.IsNull)
            return false;
        var time = DateTime.Parse(alive!);
        return (DateTime.UtcNow - time).TotalSeconds < 60; // Allow 30 seconds of delay before assuming node is dead
    }
    
    /// <summary>
    /// Returns the node currently hosting the given planet
    /// Will ensure the node exists and is alive
    /// </summary>
    public async Task<string> GetActiveNodeForPlanetAsync(long planetId)
    {
        if (_cache.HostedPlanets.ContainsKey(planetId))
            return Name; // We are hosting the planet (this is a local request)

        // Check if redis has the node who is hosting the planet
        var nodeName = await GetAssignedNodeForPlanet(planetId);

        if (nodeName is null)
        {
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Node not found for planet {PlanetId}", planetId);

            return await AssignNodeForPlanetAsync(planetId);
        }
        
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Planet {PlanetId} belongs to node {Node}", planetId, nodeName);
        
        // Check if node is alive
        if (!await IsNodeAliveAsync(nodeName))
        {
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Node {Node} is not alive, finding new node...", nodeName);
            
            return await AssignNodeForPlanetAsync(planetId);
        }
        
        return nodeName;
    }
    
    /// <summary>
    /// Returns the currently assigned node for the given planet
    /// May not be active or alive
    /// </summary>
    private async Task<string?> GetAssignedNodeForPlanet(long planetId)
    {
        var key = $"planet:{planetId}";
        var nodeName = await _nodeRecords.StringGetAsync(key);
        return nodeName;
    }

    /// <summary>
    /// Calculates and returns the node that should host the given planet
    /// </summary>
    private async Task<string> AssignNodeForPlanetAsync(long planetId)
    {
        // Check if we can host the planet
        var hosting = await TryHostPlanetAsync(planetId);
        if (hosting)
            return Name;
        
        // We can't host the planet, so we need to request another node
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Requesting another node to host {PlanetId}", planetId);

        var tryNum = 1;
        string? node = null;
        while (node is null)
        {
            // Put a request object in redis channel
            await _redisChannel.PublishAsync(_planetRequestChannel, $"{Name}:{planetId.ToString()}");
            
            // Wait 200ms * tries for a response
            await Task.Delay(200 * tryNum);
            
            // Check if planet is hosted
            node = await GetActiveNodeForPlanetAsync(planetId);

            tryNum++;
        }

        // Return node once a node has taken it
        return node;
    }

    private async Task OnPlanetRequestedAsync(ChannelMessage channelMessage)
    {
        try
        {
            var split = channelMessage.Message.ToString().Split(':');
            var planetId = long.Parse(split[1]);
            
            // Claiming is serialized with a Redis lock inside TryHostPlanetAsync.
            // Do not pre-read here: that check used to be a TOCTOU race where
            // several nodes could all begin serving the same planet.
            var hosting = await TryHostPlanetAsync(planetId);
            if (hosting && NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Taking ownership of planet {PlanetId}", planetId);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling planet request");
        }
    }

    /// <summary>
    /// Atomically claims an unassigned planet, or takes over a claim only after
    /// its current holder is confirmed dead. The short Redis lock turns the
    /// previous read/then-unconditional-write sequence into a single election.
    /// </summary>
    private async Task<bool> TryHostPlanetAsync(long planetId)
    {
        var key = $"planet:{planetId}";
        var claimLock = $"planet-claim:{planetId}";
        var ownerToken = $"{Name}:{Guid.NewGuid():N}";

        if (!await _nodeRecords.LockTakeAsync(claimLock, ownerToken, PlanetClaimLockLifetime))
            return false;

        try
        {
            var current = await _nodeRecords.StringGetAsync(key);
            if (current.HasValue && current != Name && await IsNodeAliveAsync(current!))
                return false;

            // Either no one owned it, this node already owns it, or a dead
            // owner was atomically replaced while holding the election lock.
            await _nodeRecords.StringSetAsync(key, Name);
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Node {Node} claimed planet {PlanetId}", Name, planetId);

            return true;
        }
        finally
        {
            await _nodeRecords.LockReleaseAsync(claimLock, ownerToken);
        }
    }
    
    ////////////////////////////////
    /// Inter-node communications //
    ////////////////////////////////
    
    public enum NodeEventType
    {
        Transaction,
        DirectMessage,
        DirectMessageEdit,
        DirectMessageDelete,
        VoiceModeration,
        Notification,
        Friend,
        NotificationsCleared,
        PlanetUserUpdate,
        PlanetUserDelete,
        PlanetRealtimeEviction,
        ChannelRealtimeEviction,
    }
    
    public sealed class NodeRelayEventData
    {
        public long TargetUser { get; init; }
        public long TargetPlanet { get; init; }
        public NodeEventType Type { get; init; }
        public JsonElement Payload { get; init; }
    }

    /// <summary>
    /// Relays an event to all nodes that are hosting the given user (primary nodes)
    /// </summary>
    public async Task RelayUserEventAsync(long userId, NodeEventType eventType, object data)
    {
        var userNodes = await _connectionTracker.GetPrimaryNodeConnectionsAsync(userId, _redis);
        
        NodeRelayEventData eventData = new()
        {
            Type = eventType,
            // Keep the wire format explicitly typed by NodeEventType. Deserializing
            // an object property otherwise produces JsonElement and the old casts
            // failed for every remote relay (and public fields were not serialized
            // at all under the default System.Text.Json options).
            Payload = JsonSerializer.SerializeToElement(data, data?.GetType() ?? typeof(object), RelayJsonOptions),
            TargetUser = userId,
        };
        
        var json = JsonSerializer.Serialize(eventData, RelayJsonOptions);

        foreach (var node in userNodes)
        { 
            if (node == NodeConfig.Instance.Name)
            {
                // Skip redis, run on self
                AfterNodeRelayEventAsync(eventData);
            }
            else
            {
                // Publish to the exact literal channel the remote node subscribes
                // to. Previously this omitted the "node-relay-" prefix.
                await _redisChannel.PublishAsync(
                    new RedisChannel($"node-relay-{node}", RedisChannel.PatternMode.Literal), json);
            }
        }
    }

    /// <summary>
    /// Delivers user-presence/profile changes to the node actually hosting a
    /// planet. User events are otherwise scoped to the user's primary node,
    /// which is unrelated to the planet's SignalR group in a multi-node cluster.
    /// </summary>
    public async Task RelayPlanetUserEventAsync(
        long planetId, NodeEventType eventType, Valour.Server.Models.User user, int flags = 0)
    {
        if (eventType is not (NodeEventType.PlanetUserUpdate or NodeEventType.PlanetUserDelete))
            throw new ArgumentOutOfRangeException(nameof(eventType));

        var host = await GetActiveNodeForPlanetAsync(planetId);
        var eventData = new NodeRelayEventData
        {
            TargetPlanet = planetId,
            Type = eventType,
            // Flags are part of the SignalR event contract for updates. The
            // delete path ignores them but keeping one envelope avoids a second
            // serialization protocol.
            Payload = JsonSerializer.SerializeToElement(new PlanetUserRelayPayload { User = user, Flags = flags }, RelayJsonOptions),
        };

        if (host == Name)
        {
            AfterNodeRelayEventAsync(eventData);
            return;
        }

        await _redisChannel.PublishAsync(
            new RedisChannel($"node-relay-{host}", RedisChannel.PatternMode.Literal),
            JsonSerializer.Serialize(eventData, RelayJsonOptions));
    }

    private sealed class PlanetUserRelayPayload
    {
        public Valour.Server.Models.User User { get; init; } = null!;
        public int Flags { get; init; }
    }

    private sealed class PlanetRealtimeEvictionPayload
    {
        public long UserId { get; init; }
    }

    private sealed class ChannelRealtimeEvictionPayload
    {
        public long ChannelId { get; init; }
        public long[] UserIds { get; init; } = [];
    }

    /// <summary>
    /// Routes a membership revocation to the one application node hosting the
    /// planet's SignalR groups. A delete request may arrive at a different
    /// front-end node, so doing the removal only in that request process would
    /// leave the live subscription on the host intact.
    /// </summary>
    public async Task EvictUserFromPlanetRealtimeAsync(long planetId, long userId)
    {
        var host = await GetActiveNodeForPlanetAsync(planetId);
        if (host == Name)
        {
            await EvictUserFromPlanetRealtimeLocalAsync(planetId, userId);
            return;
        }

        var eventData = new NodeRelayEventData
        {
            TargetPlanet = planetId,
            Type = NodeEventType.PlanetRealtimeEviction,
            Payload = JsonSerializer.SerializeToElement(
                new PlanetRealtimeEvictionPayload { UserId = userId }, RelayJsonOptions),
        };
        await PublishToNodeAsync(host, eventData);
    }

    /// <summary>
    /// Routes channel permission revocation to the planet host, where the
    /// connection registry for the channel group actually lives.
    /// </summary>
    public async Task EvictUsersFromChannelRealtimeAsync(
        long planetId,
        long channelId,
        IReadOnlyList<long> userIds)
    {
        if (userIds.Count == 0)
            return;

        var host = await GetActiveNodeForPlanetAsync(planetId);
        if (host == Name)
        {
            await EvictUsersFromChannelRealtimeLocalAsync(channelId, userIds);
            return;
        }

        var eventData = new NodeRelayEventData
        {
            TargetPlanet = planetId,
            Type = NodeEventType.ChannelRealtimeEviction,
            Payload = JsonSerializer.SerializeToElement(new ChannelRealtimeEvictionPayload
            {
                ChannelId = channelId,
                UserIds = userIds.Distinct().ToArray(),
            }, RelayJsonOptions),
        };
        await PublishToNodeAsync(host, eventData);
    }

    private async Task PublishToNodeAsync(string node, NodeRelayEventData eventData)
    {
        await _redisChannel.PublishAsync(
            new RedisChannel($"node-relay-{node}", RedisChannel.PatternMode.Literal),
            JsonSerializer.Serialize(eventData, RelayJsonOptions));
    }

    private async Task EvictUserFromPlanetRealtimeLocalAsync(long planetId, long userId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
        await hubService.EvictUserFromPlanetRealtimeLocalAsync(planetId, userId);
    }

    private async Task EvictUsersFromChannelRealtimeLocalAsync(long channelId, IReadOnlyList<long> userIds)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
        await hubService.EvictUsersFromChannelGroupLocalAsync(channelId, userIds);
    }

    /// <summary>
    /// This channel is called when this node is sent an event from another node
    /// </summary>
    private void OnNodeRelayEvent(ChannelMessage channelMessage)
    {
        try
        {
            var data = JsonSerializer.Deserialize<NodeRelayEventData>(channelMessage.Message.ToString(), RelayJsonOptions);
            if (data is not null)
                AfterNodeRelayEventAsync(data);
        }
        catch (JsonException e)
        {
            _logger.LogWarning(e, "Ignoring malformed node relay event");
        }
    }

    private void AfterNodeRelayEventAsync(NodeRelayEventData data)
    {
        switch (data.Type)
        {
            case NodeEventType.Transaction:
            {
                if (TryReadRelayPayload(data, out Transaction transaction))
                    OnRelayTransaction(transaction);
                break;
            }
            case NodeEventType.DirectMessage:
            {
                if (TryReadRelayPayload(data, out Message message))
                    OnRelayDirectMessage(message, data.TargetUser);
                break;
            }
            case NodeEventType.DirectMessageEdit:
            {
                if (TryReadRelayPayload(data, out Message message))
                    OnRelayDirectMessageEdit(message, data.TargetUser);
                break;
            }
            case NodeEventType.DirectMessageDelete:
            {
                if (TryReadRelayPayload(data, out Message message))
                    OnRelayDirectMessageDelete(message, data.TargetUser);
                break;
            }
            case NodeEventType.VoiceModeration:
            {
                if (TryReadRelayPayload(data, out VoiceModerationEvent moderation))
                    OnRelayVoiceModerationAction(moderation, data.TargetUser);
                break;
            }
            case NodeEventType.Notification:
            {
                if (TryReadRelayPayload(data, out Notification notification))
                    OnRelayNotification(notification, data.TargetUser);
                break;
            }
            case NodeEventType.Friend:
            {
                if (TryReadRelayPayload(data, out FriendEventData friendEvent))
                    OnRelayFriendEvent(friendEvent, data.TargetUser);
                break;
            }
            case NodeEventType.NotificationsCleared:
            {
                if (TryReadRelayPayload(data, out long targetUser))
                    OnRelayNotificationsCleared(targetUser);
                break;
            }
            case NodeEventType.PlanetUserUpdate:
            {
                if (TryReadRelayPayload(data, out PlanetUserRelayPayload update))
                    _ = _hub.Clients.Group($"p-{data.TargetPlanet}").SendAsync("User-Update", update.User, update.Flags);
                break;
            }
            case NodeEventType.PlanetUserDelete:
            {
                if (TryReadRelayPayload(data, out PlanetUserRelayPayload delete))
                    _ = _hub.Clients.Group($"p-{data.TargetPlanet}").SendAsync("User-Delete", delete.User);
                break;
            }
            case NodeEventType.PlanetRealtimeEviction:
            {
                if (TryReadRelayPayload(data, out PlanetRealtimeEvictionPayload eviction))
                    _ = EvictUserFromPlanetRealtimeLocalAsync(data.TargetPlanet, eviction.UserId);
                break;
            }
            case NodeEventType.ChannelRealtimeEviction:
            {
                if (TryReadRelayPayload(data, out ChannelRealtimeEvictionPayload eviction))
                    _ = EvictUsersFromChannelRealtimeLocalAsync(eviction.ChannelId, eviction.UserIds);
                break;
            }
        }
    }

    private bool TryReadRelayPayload<T>(NodeRelayEventData data, out T payload)
    {
        try
        {
            payload = data.Payload.Deserialize<T>(RelayJsonOptions);
            if (payload is not null)
                return true;
        }
        catch (JsonException e)
        {
            _logger.LogWarning(e, "Ignoring malformed {Type} relay payload", data.Type);
        }

        payload = default;
        return false;
    }

    private void OnRelayTransaction(Transaction transaction)
    {
        _ = _hub.Clients.Group($"u-{transaction.UserFromId}").SendAsync("Transaction-Processed", transaction);
        _ = _hub.Clients.Group($"u-{transaction.UserToId}").SendAsync("Transaction-Processed", transaction);
    }

    private void OnRelayDirectMessage(Message message, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayDirect", message);
    }
    
    private void OnRelayDirectMessageEdit(Message message, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayDirectEdit", message);
    }

    private void OnRelayDirectMessageDelete(Message message, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("DeleteMessage", message);
    }

    private void OnRelayVoiceModerationAction(VoiceModerationEvent moderation, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("Voice-Moderation-Action", moderation);
    }
    
    private void OnRelayNotification(Notification notif, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayNotification", notif);
    }

    private void OnRelayFriendEvent(FriendEventData eventData, long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayFriendEvent", eventData);
    }
    
    private void OnRelayNotificationsCleared(long targetUser)
    {
        _ = _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayNotificationsCleared");
    }
}
