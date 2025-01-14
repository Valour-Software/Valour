using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class ChannelPermissionCache
{
    private readonly ConcurrentDictionary<long, long> _cache = new();
    
    // Used to get all the cached channel keys associated with a role key
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _roleKeyToCachedChannelKeys = new();
    
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _channelIdToCachedChannelKeys = new();
    
    public long? GetChannelPermission(long key)
    {
        if (!_cache.TryGetValue(key, out var value))
            return null;
        
        return value;
    }
    
    public void Set(long roleKey, long channelId, long permissions)
    {
        var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
        
        _cache[channelKey] = permissions;
        
        if (!_channelIdToCachedChannelKeys.TryGetValue(channelId, out var idToChannelKeys))
        {
            idToChannelKeys = new();
            _channelIdToCachedChannelKeys[channelId] = idToChannelKeys;
        }
        
        idToChannelKeys.Add(roleKey);
        
        if (!_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var channelKeys))
        {
            channelKeys = new();
            _roleKeyToCachedChannelKeys[roleKey] = channelKeys;
        }
        
        channelKeys.Add(channelKey);
    }
    
    public void Remove(long roleKey, long channelId)
    {
        var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
        
        if (_cache.TryRemove(channelKey, out _))
        {
            if (_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var keys))
            {
                keys.Remove(channelKey);
            }
        }
    }
    
    public void ClearCacheForCombo(long roleKey)
    {
        if (_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key, out _);
            }
            
            keys.Clear();
        }
    }
    
    public void ClearCacheForChannel(long channelId)
    {
        if (_channelIdToCachedChannelKeys.TryGetValue(channelId, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key, out _);
            }
            
            keys.Clear();
        }
    }
    
    public void Clear()
    {
        _cache.Clear();
        _roleKeyToCachedChannelKeys.Clear();
    }
}

public class PlanetPermissionsCache
{
    public static readonly ObjectPool<List<Channel>> AccessListPool = 
        new DefaultObjectPool<List<Channel>>(new ListPooledObjectPolicy<Channel>());
    
    /// <summary>
    /// Cache for what role combinations can access channels
    /// </summary>
    private readonly ConcurrentDictionary<long, SortedServerModelList<Channel, long>> _accessCache = new();
    
    /// <summary>
    /// Cache for permissions in channels by role combination
    /// </summary>
    private readonly ChannelPermissionCache[] _channelPermissionCachesByType = new ChannelPermissionCache[3];
    
    /// <summary>
    /// Cache for authority by role combination
    /// </summary>
    private readonly ConcurrentDictionary<long, uint?> _authorityCache = new();
    
    /// <summary>
    /// Cache for planet level permissions by role combination
    /// </summary>
    private readonly ConcurrentDictionary<long, long?> _planetPermissionsCache = new();
    
    /// <summary>
    /// Maps a role to all known role combinations that include it
    /// </summary>
    private readonly ConcurrentDictionary<long, List<long>> _rolesToCombos = new();
    private readonly ConcurrentDictionary<long, object> _roleToCombosLocks = new();
    
    public PlanetPermissionsCache()
    {
        for (int i = 0; i < _channelPermissionCachesByType.Length; i++)
        {
            _channelPermissionCachesByType[i] = new ChannelPermissionCache();
        }
    }
    
    public ChannelPermissionCache GetChannelCache(ChannelTypeEnum type)
    {
        switch (type)
        {
            case ChannelTypeEnum.PlanetChat:
                return _channelPermissionCachesByType[0];
            case ChannelTypeEnum.PlanetCategory:
                return _channelPermissionCachesByType[1];
            case ChannelTypeEnum.PlanetVoice:
                return _channelPermissionCachesByType[2];
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
    
    public SortedServerModelList<Channel, long> GetChannelAccess(long roleKey)
    {
        return _accessCache.GetValueOrDefault(roleKey);
    }
    
    public List<Channel> GetEmptyAccessList()
    {
        return AccessListPool.Get();
    }

    public SortedServerModelList<Channel, long> SetChannelAccess(long roleKey, List<Channel> access)
    {
        SortedServerModelList<Channel, long> result = null;
        
        if (_accessCache.TryGetValue(roleKey, out var existing))
        {
            existing.Set(access);
            result = existing;
        }
        else
        {
            var newAccess = new SortedServerModelList<Channel, long>();
            newAccess.Set(access);
            _accessCache[roleKey] = newAccess;
            result = newAccess;
        }
        
        // Put back into pool
        AccessListPool.Return(access);
        
        return result;
    }
    
    public void ClearCacheForCombo(long roleKey)
    {
        _accessCache.TryRemove(roleKey, out _);
        _authorityCache.TryRemove(roleKey, out _);
        _planetPermissionsCache.TryRemove(roleKey, out _);
        
        foreach (var cache in _channelPermissionCachesByType)
        {
            cache.ClearCacheForCombo(roleKey);
        }
    }
    
    public void ClearCacheForChannel(long channelId)
    {
        foreach (var cache in _channelPermissionCachesByType)
        {
            cache.ClearCacheForChannel(channelId);
        }
    }
    
    public void ClearCacheForComboInChannel(long roleKey, long channelId, ChannelTypeEnum type)
    {
        var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
        var cache = GetChannelCache(type);
        cache.Remove(roleKey, channelKey);
    }
        
    public void Clear()
    {
        foreach (var cache in _channelPermissionCachesByType)
        {
            cache.Clear();
        }
        
        _accessCache.Clear();
    }
    
    public void SetAuthority(long roleKey, uint authority)
    {
        _authorityCache[roleKey] = authority;
    }
    
    public uint? GetAuthority(long roleKey)
    {
        return _authorityCache.GetValueOrDefault(roleKey);
    }
    
    public long? GetPlanetPermissions(long roleKey)
    {
        return _planetPermissionsCache.GetValueOrDefault(roleKey);
    }
    
    public void SetPlanetPermissions(long roleKey, long permissions)
    {
        _planetPermissionsCache[roleKey] = permissions;
    }
    
    /// <summary>
    /// Add a known role combination to the cache for a role
    /// </summary>
    public void AddKnownComboToRole(long roleId, long comboKey)
    {
        var lockObj = _roleToCombosLocks.GetOrAdd(roleId, _ => new object());
        lock (lockObj)
        {
            if (!_rolesToCombos.TryGetValue(roleId, out var combos))
            {
                combos = new List<long>();
                _rolesToCombos[roleId] = combos;
            }
            combos.Add(comboKey);
        }
    }
    
    /// <summary>
    /// Add a known role combination to the cache for multiple roles
    /// </summary>
    public void AddKnownComboToRoles(IEnumerable<long> roleIds, long comboKey)
    {
        foreach (var roleId in roleIds)
        {
            AddKnownComboToRole(roleId, comboKey);
        }
    }
    
    
    /// <summary>
    /// Get all known role combinations that include the given role
    /// </summary>
    public List<long>? GetCombosForRole(long roleId)
    {
        var lockObj = _roleToCombosLocks.GetOrAdd(roleId, _ => new object());
        lock (lockObj)
        {
            return _rolesToCombos.GetValueOrDefault(roleId).ToList(); // Copy to avoid threading issues
        }
    }
}