using StackExchange.Redis;
using Valour.Server.Config;

namespace Valour.Server.Services;

/// <summary>
/// Handles node communication and coordination
/// </summary>
public class NodeService
{
    private readonly ValourDB _db;
    private readonly IDatabase _nodeRecords;
    private readonly ILogger<NodeService> _logger;
    
    private readonly string _nodeAliveKey = $"alive:{NodeConfig.Instance.Name}";

    public NodeService(ValourDB db, IConnectionMultiplexer redis, ILogger<NodeService> logger)
    {
        _logger = logger;
        _db = db;
        _nodeRecords = redis.GetDatabase(1);
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
        var key = $"planet:{planetId}";
        var node = await _nodeRecords.StringGetAsync(key);
        if (node.IsNull)
            return null;
        return node;
    }

    /// <summary>
    /// Unlike GetPlanetNodeAsync, this method will request a node for the given planet if one is not found
    /// </summary>
    public async Task RequestPlanetNodeAsync(long planetId)
    {

    }
}