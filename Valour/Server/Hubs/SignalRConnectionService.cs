using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Redis;

namespace Valour.Server.Hubs;

/// <summary>
/// Tracks SignalR connections, group memberships and user sessions
/// </summary>
public class SignalRConnectionService : IDisposable
{
    private static ILogger<SignalRConnectionService> _logger;
    
    // For monitoring and diagnostics
    public static int TotalConnections => ConnectionIdentities.Count;
    public static int TotalGroups => GroupRegistry.Count;
    public static int TotalPrimaryConnections => PrimaryConnections.Count;
    
    // Authentication data
    private static readonly ConcurrentDictionary<string, AuthToken> ConnectionIdentities = new();
    
    // Primary user connections
    private static readonly ConcurrentDictionary<string, Valour.Database.PrimaryNodeConnection> PrimaryConnections = new();
    
    // Thread synchronization
    private static readonly SemaphoreSlim _registryLock = new SemaphoreSlim(1, 1);
    
    // Our single source of truth - all other structures derive from this
    private static readonly ConcurrentDictionary<string, GroupInfo> GroupRegistry = new();
    private static readonly ConcurrentDictionary<string, HashSet<string>> ConnectionToGroups = new();
    private static readonly ConcurrentDictionary<long, HashSet<string>> UserToGroups = new();
    
    public static void SetLogger(ILogger<SignalRConnectionService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// GroupInfo stores all membership data for a specific group
    /// </summary>
    private class GroupInfo
    {
        public string GroupId { get; }
        public HashSet<string> Connections { get; } = new();
        public HashSet<long> UserIds { get; } = new();
        
        // For thread safety
        private readonly object _lock = new();
        
        public GroupInfo(string groupId)
        {
            GroupId = groupId;
        }
        
        public void AddConnection(string connectionId, long? userId = null)
        {
            lock (_lock)
            {
                Connections.Add(connectionId);
                if (userId.HasValue)
                {
                    UserIds.Add(userId.Value);
                }
            }
        }
        
        public void RemoveConnection(string connectionId, long? userId = null)
        {
            lock (_lock)
            {
                Connections.Remove(connectionId);
                
                // Only remove userId if this was the last connection for this user in this group
                if (userId.HasValue && !Connections.Any(c => 
                    ConnectionIdentities.TryGetValue(c, out var token) && 
                    token?.UserId == userId.Value))
                {
                    UserIds.Remove(userId.Value);
                }
            }
        }
        
        public bool HasConnections => Connections.Count > 0;
        
        public string[] GetConnectionsCopy()
        {
            lock (_lock)
            {
                return Connections.ToArray();
            }
        }
        
        public long[] GetUserIdsCopy()
        {
            lock (_lock)
            {
                return UserIds.ToArray();
            }
        }
    }
    
    public void AddConnectionIdentity(string connectionId, AuthToken token)
    {
        if (string.IsNullOrEmpty(connectionId) || token == null)
            return;
                
        ConnectionIdentities.AddOrUpdate(connectionId, token, (key, oldToken) => token);
    }
        
    public void RemoveConnectionIdentity(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;
                
        ConnectionIdentities.TryRemove(connectionId, out _);
    }
    
    /// <summary>
    /// Gets the authentication token for a connection
    /// </summary>
    public AuthToken GetToken(string connectionId)
    {
        return ConnectionIdentities.GetValueOrDefault(connectionId);
    }
    
    /// <summary>
    /// Registers a connection authentication token
    /// </summary>
    public void RegisterConnectionIdentity(string connectionId, AuthToken token)
    {
        if (string.IsNullOrEmpty(connectionId) || token == null)
            return;
            
        ConnectionIdentities[connectionId] = token;
    }
    
    /// <summary>
    /// Tracks group membership for a connection
    /// </summary>
    public async Task TrackGroupMembershipAsync(string groupId, HubCallerContext context)
    {
        if (string.IsNullOrEmpty(groupId) || context == null)
        {
            _logger?.LogWarning("Attempted to track null/empty group or with null context");
            return;
        }

        var connectionId = context.ConnectionId;
        long? userId = null;
        
        // Get the user ID from the connection identity
        if (ConnectionIdentities.TryGetValue(connectionId, out var token) && token != null)
        {
            userId = token.UserId;
        }
        
        // Get or create the group info
        var groupInfo = GroupRegistry.GetOrAdd(groupId, id => new GroupInfo(id));
        
        // Add connection to group
        groupInfo.AddConnection(connectionId, userId);
        
        // Update connection-to-groups mapping
        await _registryLock.WaitAsync();
        try
        {
            var connectionGroups = ConnectionToGroups.GetOrAdd(connectionId, _ => new HashSet<string>());
            connectionGroups.Add(groupId);
            
            // Update user-to-groups mapping if we have a user ID
            if (userId.HasValue)
            {
                var userGroups = UserToGroups.GetOrAdd(userId.Value, _ => new HashSet<string>());
                userGroups.Add(groupId);
            }
        }
        finally
        {
            _registryLock.Release();
        }
        
        _logger?.LogTrace($"Connection {connectionId} added to group {groupId}");
    }
    
    /// <summary>
    /// Removes group membership tracking for a connection
    /// </summary>
    public async Task UntrackGroupMembershipAsync(string groupId, HubCallerContext context)
    {
        if (string.IsNullOrEmpty(groupId) || context == null)
            return;

        var connectionId = context.ConnectionId;
        long? userId = null;
        
        // Get the user ID from the connection identity
        if (ConnectionIdentities.TryGetValue(connectionId, out var token) && token != null)
        {
            userId = token.UserId;
        }
        
        // Remove from group info if it exists
        if (GroupRegistry.TryGetValue(groupId, out var groupInfo))
        {
            groupInfo.RemoveConnection(connectionId, userId);
            
            // Remove empty groups
            if (!groupInfo.HasConnections)
            {
                GroupRegistry.TryRemove(groupId, out _);
                _logger?.LogTrace($"Removed empty group {groupId}");
            }
        }
        
        // Update connection-to-groups mapping
        await _registryLock.WaitAsync();
        try
        {
            if (ConnectionToGroups.TryGetValue(connectionId, out var connectionGroups))
            {
                connectionGroups.Remove(groupId);
                
                // Remove empty collections
                if (connectionGroups.Count == 0)
                {
                    ConnectionToGroups.TryRemove(connectionId, out _);
                }
            }
            
            // Update user-to-groups mapping
            if (userId.HasValue && UserToGroups.TryGetValue(userId.Value, out var userGroups))
            {
                // Check if this is the last connection for this user in this group
                bool isLastConnection = !GroupRegistry.TryGetValue(groupId, out var group) ||
                                      !group.GetUserIdsCopy().Contains(userId.Value);
                
                if (isLastConnection)
                {
                    userGroups.Remove(groupId);
                    
                    if (userGroups.Count == 0)
                    {
                        UserToGroups.TryRemove(userId.Value, out _);
                    }
                }
            }
        }
        finally
        {
            _registryLock.Release();
        }
        
        _logger?.LogTrace($"Connection {connectionId} removed from group {groupId}");
    }
    
    /// <summary>
    /// Removes all group memberships for a connection
    /// </summary>
    public async Task RemoveAllMembershipsAsync(HubCallerContext context)
    {
        if (context == null)
            return;
            
        var connectionId = context.ConnectionId;
        
        // Get all groups for connection
        HashSet<string> groupsCopy = null;
        
        await _registryLock.WaitAsync();
        try
        {
            if (ConnectionToGroups.TryGetValue(connectionId, out var groups) && groups != null)
            {
                groupsCopy = new HashSet<string>(groups);
            }
        }
        finally
        {
            _registryLock.Release();
        }
        
        if (groupsCopy == null)
            return;
            
        // Remove from each group
        foreach (var groupId in groupsCopy)
        {
            await UntrackGroupMembershipAsync(groupId, context);
        }
        
        // Remove identity
        ConnectionIdentities.TryRemove(connectionId, out _);
        
        _logger?.LogTrace($"Removed all memberships for connection {connectionId}");
    }
    
    /// <summary>
    /// Gets all connections for a group
    /// </summary>
    public string[] GetGroupConnections(string groupId)
    {
        if (GroupRegistry.TryGetValue(groupId, out var groupInfo))
        {
            return groupInfo.GetConnectionsCopy();
        }
        
        return Array.Empty<string>();
    }
    
    /// <summary>
    /// Gets all user IDs in a group
    /// </summary>
    public long[] GetGroupUserIds(string groupId)
    {
        if (GroupRegistry.TryGetValue(groupId, out var groupInfo))
        {
            return groupInfo.GetUserIdsCopy();
        }
        
        return Array.Empty<long>();
    }
    
    /// <summary>
    /// Gets all groups a connection is member of
    /// </summary>
    public string[] GetConnectionGroups(string connectionId)
    {
        if (ConnectionToGroups.TryGetValue(connectionId, out var groups))
        {
            return groups.ToArray();
        }
        
        return Array.Empty<string>();
    }
    
    /// <summary>
    /// Gets all groups a user is member of
    /// </summary>
    public string[] GetUserGroups(long userId)
    {
        if (UserToGroups.TryGetValue(userId, out var groups))
        {
            return groups.ToArray();
        }
        
        return Array.Empty<string>();
    }
    
    /// <summary>
    /// Gets all primary node connections for a user
    /// </summary>
    public async Task<List<string>> GetPrimaryNodeConnectionsAsync(long userId, IConnectionMultiplexer redis)
    {
        if (redis == null)
            throw new ArgumentNullException(nameof(redis));
            
        var rdb = redis.GetDatabase(RedisDbTypes.Cluster);
        var connections = new List<string>();
        
        try
        {
            var scan = rdb.SetScanAsync(new RedisKey($"user:{userId}"));
            var seenNodes = new HashSet<string>();
            
            await foreach (var value in scan)
            {
                var valueStr = value.ToString();
                var split = valueStr.Split(':');
                
                if (split.Length < 2)
                {
                    _logger?.LogWarning($"Malformed Redis value: {valueStr}");
                    continue;
                }
                
                var node = split[0];
                if (seenNodes.Add(node))
                {
                    connections.Add(node);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error retrieving primary connections for user {userId}");
            throw;
        }
        
        return connections;
    }
    
    /// <summary>
    /// Adds a primary connection for a user
    /// </summary>
    public async Task AddPrimaryConnectionAsync(long userId, HubCallerContext context, IConnectionMultiplexer redis)
    {
        if (context == null || redis == null)
            throw new ArgumentNullException(context == null ? nameof(context) : nameof(redis));
            
        var connectionId = context.ConnectionId;
        var nodeName = NodeConfig.Instance.Name;
        
        var conn = new Valour.Database.PrimaryNodeConnection
        {
            ConnectionId = connectionId,
            UserId = userId,
            OpenTime = DateTime.UtcNow,
            NodeId = nodeName
        };
        
        // Add to collection
        PrimaryConnections[connectionId] = conn;
        
        var rdb = redis.GetDatabase(RedisDbTypes.Cluster);
        var userIdStr = userId.ToString();
        var connectionValue = $"{userIdStr}:{connectionId}";
        var nodeValue = $"{nodeName}:{connectionId}";
        
        try
        {
            // Execute Redis operations with proper error handling
            await Task.WhenAll(
                rdb.SetAddAsync($"node:{nodeName}", connectionValue),
                rdb.SetAddAsync($"user:{userIdStr}", nodeValue)
            );
            
            _logger?.LogTrace($"Added primary connection {connectionId} for user {userId}");
        }
        catch (Exception ex)
        {
            // Clean up on failure
            PrimaryConnections.TryRemove(connectionId, out _);
            _logger?.LogError(ex, $"Failed to add primary connection for user {userId}");
            throw;
        }
    }
    
    /// <summary>
    /// Removes a primary connection
    /// </summary>
    public async Task RemovePrimaryConnectionAsync(HubCallerContext context, IConnectionMultiplexer redis)
    {
        if (context == null || redis == null)
            return;
            
        var connectionId = context.ConnectionId;
        
        // Was not a primary connection
        if (!PrimaryConnections.TryRemove(connectionId, out var connection))
            return;
        
        // Get the user ID
        long userId;
        if (connection != null)
        {
            userId = connection.UserId;
        }
        else if (ConnectionIdentities.TryGetValue(connectionId, out var token) && token != null)
        {
            userId = token.UserId;
        }
        else
        {
            _logger?.LogWarning($"Failed to find user ID for connection {connectionId} during removal");
            return;
        }
        
        var nodeName = NodeConfig.Instance.Name;
        var rdb = redis.GetDatabase(RedisDbTypes.Cluster);
        var userIdStr = userId.ToString();
        
        try
        {
            // Execute Redis operations
            await Task.WhenAll(
                rdb.SetRemoveAsync($"node:{nodeName}", $"{userIdStr}:{connectionId}"),
                rdb.SetRemoveAsync($"user:{userIdStr}", $"{nodeName}:{connectionId}")
            );
            
            _logger?.LogTrace($"Removed primary connection {connectionId} for user {userId}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Error removing primary connection from Redis for user {userId}");
            // Not re-adding to collection since removal is still desirable
        }
    }
    
    /// <summary>
    /// Gets all registered group IDs without creating a copy
    /// </summary>
    public IEnumerable<string> GetAllGroups()
    {
        return GroupRegistry.Keys;
    }
    
    /// <summary>
    /// Cleans up all resources and connections
    /// </summary>
    public static void Cleanup()
    {
        ConnectionIdentities.Clear();
        GroupRegistry.Clear();
        ConnectionToGroups.Clear();
        UserToGroups.Clear();
        PrimaryConnections.Clear();
        
        _logger?.LogInformation("Connection tracker resources cleaned up");
    }
    
    public void Dispose()
    {
        _registryLock?.Dispose();
    }
    
    /// <summary>
    /// Gets diagnostic information about the current state
    /// </summary>
    public string GetDiagnosticInfo()
    {
        return $"Connections: {TotalConnections}, Groups: {TotalGroups}, Primary Connections: {TotalPrimaryConnections}";
    }
}
