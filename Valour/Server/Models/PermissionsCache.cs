using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class ChannelPermissionCache
{
    private readonly ConcurrentDictionary<long, long> _cache = new();
    
    // Used to get all the cached channel keys associated with a role key
    private readonly ConcurrentDictionary<long, List<long>> _roleKeyToCachedChannelKeys = new();
    
    public long? GetChannelPermission(long key)
    {
        return _cache.GetValueOrDefault(key);
    }
    
    public void Set(long roleKey, long channelKey, long permissions)
    {
        _cache[channelKey] = permissions;
        
        if (!_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var channelKeys))
        {
            channelKeys = new List<long>();
            _roleKeyToCachedChannelKeys[roleKey] = channelKeys;
        }
        
        channelKeys.Add(channelKey);
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
    private readonly ChannelPermissionCache[] _permissionCachesByType = new ChannelPermissionCache[3];
    
    /// <summary>
    /// Cache for authority by role combination
    /// </summary>
    private readonly ConcurrentDictionary<long, uint?> _authorityCache = new();
    
    public PlanetPermissionsCache()
    {
        for (int i = 0; i < _permissionCachesByType.Length; i++)
        {
            _permissionCachesByType[i] = new ChannelPermissionCache();
        }
    }
    
    public ChannelPermissionCache GetChannelCache(ChannelTypeEnum type)
    {
        switch (type)
        {
            case ChannelTypeEnum.PlanetChat:
                return _permissionCachesByType[0];
            case ChannelTypeEnum.PlanetCategory:
                return _permissionCachesByType[1];
            case ChannelTypeEnum.PlanetVoice:
                return _permissionCachesByType[2];
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
        
        foreach (var cache in _permissionCachesByType)
        {
            cache.ClearCacheForCombo(roleKey);
        }
    }
        
    public void Clear()
    {
        foreach (var cache in _permissionCachesByType)
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
}