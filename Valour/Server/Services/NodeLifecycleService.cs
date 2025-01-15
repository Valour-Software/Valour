#nullable enable

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Hubs;
using Valour.Server.Redis;

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
    
    private readonly ModelCacheService _cache;
    
    private readonly string _nodeAliveKey = $"alive:{NodeConfig.Instance.Name}";
    
    private readonly RedisChannel _nodeRelayChannel;
    private readonly RedisChannel _planetRequestChannel;
    

    // Hosted planets //
    
    public NodeLifecycleService(IConnectionMultiplexer redis, ILogger<NodeLifecycleService> logger, IHubContext<CoreHub> hub, ModelCacheService cache)
    {
        _hub = hub;
        _cache = cache;
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
            
            // Check if someone else already is hosting
            var currentHost = await GetAssignedNodeForPlanet(planetId);
            if (currentHost is not null)
                return;
            
            // Check if we can host the planet
            var hosting = await TryHostPlanetAsync(planetId);
            if (hosting && NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Taking ownership of planet {PlanetId}", planetId);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling planet request");
        }
    }

    private async Task AnnouncePlanetHostedAsync(long planetId)
    {
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Taking ownership of planet {PlanetId}", planetId);

        var key = $"planet:{planetId}";
        await _nodeRecords.StringSetAsync(key, Name);
    }
    
    /// <summary>
    /// Returns whether this node is capable of hosting the given planet.
    /// Used for determining where to place unhosted planets.
    /// </summary>
    private async Task<bool> TryHostPlanetAsync(long planetId)
    {  
        // For now we just assume we can
        
        // Announce that we are hosting the planet
        await AnnouncePlanetHostedAsync(planetId);
        
        return true;
    }
    
    ////////////////////////////////
    /// Inter-node communications //
    ////////////////////////////////
    
    public enum NodeEventType
    {
        Transaction,
        DirectMessage,
        DirectMessageEdit,
        Notification,
        Friend,
        NotificationsCleared,
    }
    
    public struct NodeRelayEventData
    {
        public long TargetUser;
        public NodeEventType Type;
        public object Payload;
    }

    /// <summary>
    /// Relays an event to all nodes that are hosting the given user (primary nodes)
    /// </summary>
    public async Task RelayUserEventAsync(long userId, NodeEventType eventType, object data)
    {
        var userNodes = await ConnectionTracker.GetPrimaryNodeConnectionsAsync(userId, _redis);
        
        NodeRelayEventData eventData = new()
        {
            Type = eventType,
            Payload = data,
            TargetUser = userId,
        };
        
        var json = JsonSerializer.Serialize(eventData);

        foreach (var node in userNodes)
        { 
            if (node == NodeConfig.Instance.Name)
            {
                // Skip redis, run on self
                AfterNodeRelayEventAsync(eventData);
            }
            else
            {
                // Publish to pubsub channel for other nodes
                await _redisChannel.PublishAsync(node, json);
            }
        }
    }

    /// <summary>
    /// This channel is called when this node is sent an event from another node
    /// </summary>
    private void OnNodeRelayEvent(ChannelMessage channelMessage)
    {
        var data = JsonSerializer.Deserialize<NodeRelayEventData>(channelMessage.Message.ToString());
        AfterNodeRelayEventAsync(data);
    }

    private void AfterNodeRelayEventAsync(NodeRelayEventData data)
    {
        switch (data.Type)
        {
            case NodeEventType.Transaction:
            {
                var transaction = (Transaction) data.Payload;
                OnRelayTransaction(transaction);
                break;
            }
            case NodeEventType.DirectMessage:
            {
                var message = (Message) data.Payload;
                OnRelayDirectMessage(message, data.TargetUser);
                break;
            }
            case NodeEventType.DirectMessageEdit:
            {
                var message = (Message) data.Payload;
                OnRelayDirectMessageEdit(message, data.TargetUser);
                break;
            }
            case NodeEventType.Notification:
            {
                var notification = (Notification) data.Payload;
                OnRelayNotification(notification, data.TargetUser);
                break;
            }
            case NodeEventType.Friend:
            {
                var friendEvent = (FriendEventData) data.Payload;
                OnRelayFriendEvent(friendEvent, data.TargetUser);
                break;
            }
            case NodeEventType.NotificationsCleared:
            {
                OnRelayNotificationsCleared((long)data.Payload);
                break;
            }
        }
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