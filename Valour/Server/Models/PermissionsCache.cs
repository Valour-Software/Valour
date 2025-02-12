using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class ChannelPermissionCache
{
    private readonly ConcurrentDictionary<long, long?> _cache = new();
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _roleKeyToCachedChannelKeys = new();
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _channelIdToCachedChannelKeys = new();

    public long? GetChannelPermission(long key)
    {
        _cache.TryGetValue(key, out long? value);
        return value;
    }

    public void Set(long roleKey, long channelId, long permissions)
    {
        var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
        _cache[channelKey] = permissions;

        // Record by channel ID.
        var set1 = _channelIdToCachedChannelKeys.GetOrAdd(channelId, _ => new ConcurrentHashSet<long>());
        set1.Add(roleKey);

        // Record by role key.
        var set2 = _roleKeyToCachedChannelKeys.GetOrAdd(roleKey, _ => new ConcurrentHashSet<long>());
        set2.Add(channelKey);
    }

    public void Remove(long roleKey, long channelId)
    {
        var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
        if (_cache.TryRemove(channelKey, out _))
        {
            if (_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var set))
                set.Remove(channelKey);
        }
    }

    public void ClearCacheForCombo(long roleKey)
    {
        if (_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var keys))
        {
            foreach (var key in keys)
                _cache.TryRemove(key, out _);
            keys.Clear();
        }
    }

    public void ClearCacheForCombo(long roleKey, long channelId)
    {
        if (_roleKeyToCachedChannelKeys.TryGetValue(roleKey, out var keys))
        {
            var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
            if (keys.Contains(channelKey))
            {
                _cache.TryRemove(channelKey, out _);
                keys.Remove(channelKey);
            }
        }
    }

    public void ClearCacheForChannel(long channelId)
    {
        if (_channelIdToCachedChannelKeys.TryGetValue(channelId, out var keys))
        {
            foreach (var roleKey in keys)
            {
                var channelKey = PlanetPermissionService.GetRoleChannelComboKey(roleKey, channelId);
                _cache.TryRemove(channelKey, out _);
            }
            keys.Clear();
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _roleKeyToCachedChannelKeys.Clear();
        _channelIdToCachedChannelKeys.Clear();
    }
}

public class PlanetPermissionsCache
{
    public static readonly ObjectPool<List<Channel>> AccessListPool =
        new DefaultObjectPool<List<Channel>>(new ListPooledObjectPolicy<Channel>());

    private readonly ConcurrentDictionary<long, SortedServerModelList<Channel, long>> _accessCache = new();
    private readonly ChannelPermissionCache[] _channelPermissionCachesByType = new ChannelPermissionCache[3];
    private readonly ConcurrentDictionary<long, uint?> _authorityCache = new();
    private readonly ConcurrentDictionary<long, long?> _planetPermissionsCache = new();

    // Replacing the roles-to-combos mapping with a thread-safe dictionary of hash sets.
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _rolesToCombos = new();

    // NEW: Inverse mapping: channel ID -> lazy async computed list of role–hash keys.
    public ConcurrentDictionary<long, Lazy<Task<List<long>>>> ChannelAccessRoleComboCache { get; } =
        new ConcurrentDictionary<long, Lazy<Task<List<long>>>>();

    public PlanetPermissionsCache()
    {
        _channelPermissionCachesByType[0] = new ChannelPermissionCache();
        _channelPermissionCachesByType[1] = new ChannelPermissionCache();
        _channelPermissionCachesByType[2] = new ChannelPermissionCache();
    }

    public ChannelPermissionCache GetChannelCache(ChannelTypeEnum type)
    {
        return type switch
        {
            ChannelTypeEnum.PlanetChat => _channelPermissionCachesByType[0],
            ChannelTypeEnum.PlanetCategory => _channelPermissionCachesByType[1],
            ChannelTypeEnum.PlanetVoice => _channelPermissionCachesByType[2],
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    public ModelListSnapshot<Channel, long>? GetChannelAccess(long roleKey)
    {
        _accessCache.TryGetValue(roleKey, out var access);
        return access?.Snapshot;
    }

    public List<Channel> GetEmptyAccessList() => AccessListPool.Get();

    public SortedServerModelList<Channel, long> SetChannelAccess(long roleKey, IEnumerable<Channel> access)
    {
        SortedServerModelList<Channel, long> result;
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

        return result;
    }

    public void ClearCacheForCombo(long roleKey)
    {
        _accessCache.TryRemove(roleKey, out _);
        _authorityCache.TryRemove(roleKey, out _);
        _planetPermissionsCache.TryRemove(roleKey, out _);
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForCombo(roleKey);
    }

    public void ClearCacheForComboAndChannel(long roleKey, long channelId)
    {
        _accessCache.TryRemove(roleKey, out _);
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForCombo(roleKey, channelId);
    }

    public void ClearCacheForChannel(long channelId)
    {
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForChannel(channelId);

        ClearChannelAccessRoleComboCache(channelId);
    }

    public void ClearCacheForComboInChannel(long roleKey, long channelId, ChannelTypeEnum type)
    {
        var cache = GetChannelCache(type);
        cache.Remove(roleKey, channelId);
    }

    public void Clear()
    {
        foreach (var cache in _channelPermissionCachesByType)
            cache.Clear();

        _accessCache.Clear();
        ChannelAccessRoleComboCache.Clear();
    }

    public void SetAuthority(long roleKey, uint authority) => _authorityCache[roleKey] = authority;

    public uint? GetAuthority(long roleKey) => _authorityCache.GetValueOrDefault(roleKey);

    public long? GetPlanetPermissions(long roleKey) => _planetPermissionsCache.GetValueOrDefault(roleKey);

    public void SetPlanetPermissions(long roleKey, long permissions) => _planetPermissionsCache[roleKey] = permissions;

    // NEW: Instead of manual locks, we use a thread-safe hash set.
    public void AddKnownComboToRole(long roleId, long comboKey)
    {
        var set = _rolesToCombos.GetOrAdd(roleId, _ => new ConcurrentHashSet<long>());
        set.Add(comboKey);
    }

    public List<long>? GetCombosForRole(long roleId)
    {
        if (_rolesToCombos.TryGetValue(roleId, out var set))
            return set.ToList();
        return null;
    }
    
    public void ClearChannelAccessRoleComboCache(long channelId)
    {
        ChannelAccessRoleComboCache.TryRemove(channelId, out _);
    }

    public void ClearAllChannelAccessRoleComboCache() => ChannelAccessRoleComboCache.Clear();
}