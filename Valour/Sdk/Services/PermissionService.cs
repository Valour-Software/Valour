﻿using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
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
    
    public async ValueTask<PermissionsNode?> FetchPermissionsNodeAsync(PermissionsNodeKey key, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchPermissionsNodeAsync(key, planet, skipCache);
    }
    
    public async ValueTask<PermissionsNode?> FetchPermissionsNodeAsync(PermissionsNodeKey key, Planet planet, bool skipCache = false)
    {
        if (!skipCache && 
            _cache.PermNodeKeyToId.TryGetValue(key, out var id) &&
            planet.PermissionsNodes.TryGet(id, out var cached))
            return cached;
        
        var permNode = (await planet.Node.GetJsonAsync<PermissionsNode>(
            ISharedPermissionsNode.GetIdRoute(key.TargetId, key.RoleId, key.TargetType), 
            true)).Data;
        
        if (permNode is null)
            return null;

        return permNode.Sync(_client);
    }
    
    public async Task<List<PermissionsNode>> FetchPermissionsNodesByRoleAsync(long roleId, Planet planet)
    {
        var permissionNodes = (await planet.Node.GetJsonAsync<List<PermissionsNode>>($"{ISharedPlanetRole.GetIdRoute(planet.Id, roleId)}/nodes")).Data;
        return permissionNodes.SyncAll(_client);
    }
}