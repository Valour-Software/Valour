using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

public class PermissionService : ServiceBase
{
    private static readonly LogOptions LogOptions = new(
        "PermissionService",
        "#0083ab",
        "#ab0055",
        "#ab8900"
    );
    
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    
    public PermissionService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async ValueTask<PermissionsNode> FetchPermissionsNodeAsync(PermissionsNodeKey key, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchPermissionsNodeAsync(key, planet, skipCache);
    }
    
    public async ValueTask<PermissionsNode> FetchPermissionsNodeAsync(PermissionsNodeKey key, Planet planet, bool skipCache = false)
    {
        if (!skipCache && 
            _cache.PermNodeKeyToId.TryGetValue(key, out var id) &&
            _cache.PermissionsNodes.TryGet(id, out var cached))
            return cached;
        
        var permNode = (await planet.Node.GetJsonAsync<PermissionsNode>(
            ISharedPermissionsNode.GetIdRoute(key.TargetId, key.RoleId, key.TargetType), 
            true)).Data;
        
        return _cache.Sync(permNode);
    }
}