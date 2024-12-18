using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Server.Hubs;
using Valour.Server.Redis;

namespace Valour.Server.Services;

/// <summary>
/// Handles node communication and coordination
/// </summary>
public class NodeService
{
    // Local node information
    public readonly string Name;
    public readonly string Location;
    public readonly string Version;
    
    private readonly IDatabase _nodeRecords;
    private readonly ILogger<NodeService> _logger;
    private readonly ISubscriber _redisChannel;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<CoreHub> _hub;
    
    private readonly HostedPlanetService _hostedPlanetService;
    

    private readonly string _nodeAliveKey = $"alive:{NodeConfig.Instance.Name}";

    public NodeService(IConnectionMultiplexer redis, ILogger<NodeService> logger, IHubContext<CoreHub> hub, HostedPlanetService hostedPlanetService)
    {
        _hub = hub;
        _hostedPlanetService = hostedPlanetService;
        _logger = logger;
        _redis = redis;
        _nodeRecords = redis.GetDatabase(RedisDbTypes.Cluster);
        _redisChannel = redis.GetSubscriber();

        _redisChannel.Subscribe("planet-requests", OnPlanetRequestedAsync);
        _redisChannel.Subscribe($"node-relay-{NodeConfig.Instance.Name}", OnNodeRelayEvent);
        
        var config = NodeConfig.Instance;
        Name = config.Name;
        Location = config.Location;
        Version = typeof(Valour.Shared.Models.ISharedUser).Assembly.GetName().Version.ToString();
    }

    /// <summary>
    /// Announces that a node is live and ready to receive requests
    /// </summary>
    public async Task AnnounceNode()
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
        await _nodeRecords.StringSetAsync(_nodeAliveKey, DateTime.UtcNow.ToString());
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
        var time = DateTime.Parse(alive);
        return (DateTime.UtcNow - time).TotalSeconds < 60; // Allow 30 seconds of delay before assuming node is dead
    }
    
    /// <summary>
    /// Returns the node for the given planet id
    /// </summary>
    public async Task<string> GetNodeNameForPlanetAsync(long planetId)
    {
        if (_hostedPlanetService.IsHosted(planetId))
            return Name; // We are hosting the planet (this is a local request)
        
        var key = $"planet:{planetId}";
        var node = await _nodeRecords.StringGetAsync(key);
        if (node.IsNull)
            return null;

        // We used to host this node - maybe we restarted.
        // Add back to planet list and continue hosting.
        if (node == Name)
        {
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Resuming hosting of {PlanetId}", planetId);
            
            _hostedPlanets.Add(planetId);
        }
        
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Planet {PlanetId} belongs to node {Node}", planetId, node);
        
        // Ensure node is alive
        var alive = await IsNodeAliveAsync(node);
        if (!alive)
        {
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation("Node {Node} is dead...", node);
            
            return null;
        }

        return node;
    }

    /// <summary>
    /// Unlike GetPlanetNodeAsync, this method will request a node for the given planet if one is not found
    /// </summary>
    public async Task<string> RequestPlanetNodeAsync(long planetId)
    {
        // Check if planet is already hosted
        var location = await GetNodeNameForPlanetAsync(planetId);
        if (location is not null)
            return location;
        
        // Check if we can host the planet
        var canHost = CanHostPlanetAsync(planetId);
        if (canHost)
        {
            await AnnouncePlanetHostedAsync(planetId);
            return Name; // Return ourselves
        }
        
        // We can't host the planet, so we need to request another node
        
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Requesting another node to host {PlanetId}", planetId);

        var tryNum = 1;
        string node = null;
        while (node is null)
        {
            // Put a request object in redis channel
            await _redisChannel.PublishAsync("planet-requests", $"{Name}:{planetId.ToString()}");
            
            // Wait 200ms * tries for a response
            await Task.Delay(200 * tryNum);
            
            // Check if planet is hosted
            node = await GetNodeNameForPlanetAsync(planetId);

            tryNum++;
        }

        // Return node once a node has taken it
        return node;
    }

    public void OnPlanetRequestedAsync(RedisChannel channel, RedisValue value)
    {
        try
        {
            var split = value.ToString().Split(':');
            var planetId = long.Parse(split[1]);

            Task.Run(async () =>
            {
                // Check if someone else already is hosting
                var currentHost = await GetNodeNameForPlanetAsync(planetId);
                if (currentHost is not null)
                    return;
                
                // Check if we can host the planet
                var canHost = CanHostPlanetAsync(planetId);
                if (canHost)
                {
                    await AnnouncePlanetHostedAsync(planetId);
                }
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling planet request");
        }
    }

    public async Task AnnouncePlanetHostedAsync(long planetId)
    {
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation("Taking ownership of planet {PlanetId}", planetId);
        
        _hostedPlanets.Add(planetId);
        var key = $"planet:{planetId}";
        await _nodeRecords.StringSetAsync(key, Name);
    }
    
    /// <summary>
    /// Returns whether this node is capable of hosting the given planet.
    /// Used for determining where to place unhosted planets.
    /// </summary>
    private bool CanHostPlanetAsync(long planetId)
    {
        // For now we just assume we can
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
    private void OnNodeRelayEvent(RedisChannel channel, RedisValue value)
    {
        var data = JsonSerializer.Deserialize<NodeRelayEventData>(value.ToString());
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
        _hub.Clients.Group($"u-{transaction.UserFromId}").SendAsync("Transaction-Processed", transaction);
        _hub.Clients.Group($"u-{transaction.UserToId}").SendAsync("Transaction-Processed", transaction);
    }

    private void OnRelayDirectMessage(Message message, long targetUser)
    {
        _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayDirect", message);
    }
    
    private void OnRelayDirectMessageEdit(Message message, long targetUser)
    {
        _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayDirectEdit", message);
    }
    
    private void OnRelayNotification(Notification notif, long targetUser)
    {
        _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayNotification", notif);
    }

    private void OnRelayFriendEvent(FriendEventData eventData, long targetUser)
    {
        _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayFriendEvent", eventData);
    }
    
    private void OnRelayNotificationsCleared(long targetUser)
    {
        _hub.Clients.Group($"u-{targetUser}").SendAsync("RelayNotificationsCleared");
    }
}