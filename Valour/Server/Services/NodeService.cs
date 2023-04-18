using StackExchange.Redis;
using Valour.Server.Config;
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
    public HashSet<long> Planets { get; }
    
    private readonly IDatabase _nodeRecords;
    private readonly ILogger<NodeService> _logger;
    private readonly ISubscriber _redisChannel;
    
    private readonly string _nodeAliveKey = $"alive:{NodeConfig.Instance.Name}";

    public NodeService(IConnectionMultiplexer redis, ILogger<NodeService> logger)
    {
        _logger = logger;
        _nodeRecords = redis.GetDatabase(RedisDbTypes.Cluster);
        _redisChannel = redis.GetSubscriber();

        _redisChannel.Subscribe("planet-requests", OnPlanetRequestedAsync);
        
        var config = NodeConfig.Instance;
        Name = config.Name;
        Location = config.Location;
        Version = typeof(Valour.Shared.Models.ISharedUser).Assembly.GetName().Version.ToString();

        Planets = new();
    }

    /// <summary>
    /// Returns if the given planet is hosted on this node
    /// </summary>
    public async Task<bool> IsPlanetHostedLocally(long planetId)
    {
        if (Planets.Contains((planetId)))
            return true;

        return await GetPlanetNodeAsync(planetId) == Name;
    }

    /// <summary>
    /// Announces that a node is live and ready to receive requests
    /// </summary>
    public async Task AnnounceNode()
    {
        _logger.LogInformation($"Announcing node {NodeConfig.Instance.Name} at {NodeConfig.Instance.Location}");
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
    public async Task<string> GetPlanetNodeAsync(long planetId)
    {
        if (Planets.Contains(planetId))
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
                _logger.LogInformation($"Resuming hosting of {planetId}");
            
            Planets.Add(planetId);
        }
        
        if (NodeConfig.Instance.LogInfo)
            _logger.LogInformation($"Planet {planetId} belongs to node {node}");
        
        // Ensure node is alive
        var alive = await IsNodeAliveAsync(node);
        if (!alive)
        {
            if (NodeConfig.Instance.LogInfo)
                _logger.LogInformation($"Node {node} is dead...");
            
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
        var location = await GetPlanetNodeAsync(planetId);
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
            _logger.LogInformation($"Requesting another node to host {planetId}");

        var tryNum = 1;
        string node = null;
        while (node is null)
        {
            // Put a request object in redis channel
            await _redisChannel.PublishAsync("planet-requests", $"{Name}:{planetId.ToString()}");
            
            // Wait 200ms * tries for a response
            await Task.Delay(200 * tryNum);
            
            // Check if planet is hosted
            node = await GetPlanetNodeAsync(planetId);

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
                var currentHost = await GetPlanetNodeAsync(planetId);
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
            _logger.LogInformation($"Taking ownership of planet {planetId}");
        
        Planets.Add(planetId);
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
}