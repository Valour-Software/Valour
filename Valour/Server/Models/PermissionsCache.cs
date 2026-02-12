using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class ChannelPermissionCache
{
    private readonly ConcurrentDictionary<long, long?> _cache = new();
    private readonly ConcurrentDictionary<PlanetRoleMembership, ConcurrentHashSet<long>> _roleKeyToCachedChannelKeys = new();
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _channelIdToCachedChannelKeys = new();

    public long? GetChannelPermission(long key)
    {
        _cache.TryGetValue(key, out long? value);
        return value;
    }

    public void Set(PlanetRoleMembership roleMembership, long channelId, long permissions)
    {
        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(roleMembership, channelId);
        _cache[channelKey] = permissions;

        // Record by channel ID.
        var set1 = _channelIdToCachedChannelKeys.GetOrAdd(channelId, _ => new ConcurrentHashSet<long>());
        set1.Add(channelKey);

        // Record by role key.
        var set2 = _roleKeyToCachedChannelKeys.GetOrAdd(roleMembership, _ => new ConcurrentHashSet<long>());
        set2.Add(channelKey);
    }

    public void Remove(PlanetRoleMembership roleMembership, long channelId)
    {
        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(roleMembership, channelId);
        if (_cache.TryRemove(channelKey, out _))
        {
            if (_roleKeyToCachedChannelKeys.TryGetValue(roleMembership, out var set))
                set.Remove(channelKey);
        }
    }

    public void ClearCacheForCombo(PlanetRoleMembership roleMembership)
    {
        if (_roleKeyToCachedChannelKeys.TryGetValue(roleMembership, out var keys))
        {
            foreach (var key in keys)
                _cache.TryRemove(key, out _);
            keys.Clear();
        }
    }

    public void ClearCacheForCombo(PlanetRoleMembership roleMembership, long channelId)
    {
        if (_roleKeyToCachedChannelKeys.TryGetValue(roleMembership, out var keys))
        {
            var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(roleMembership, channelId);
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
            foreach (var channelKey in keys)
            {
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

    private readonly ConcurrentDictionary<PlanetRoleMembership, SortedServerModelList<Channel, long>> _accessCache = new();
    private readonly ChannelPermissionCache[] _channelPermissionCachesByType = new ChannelPermissionCache[3];
    private readonly ConcurrentDictionary<PlanetRoleMembership, uint?> _authorityCache = new();
    private readonly ConcurrentDictionary<PlanetRoleMembership, long?> _planetPermissionsCache = new();

    /// <summary>
    /// Generation counter incremented on every cache invalidation.
    /// Used to detect when a long-running permission computation has been
    /// racing with a concurrent invalidation, preventing stale data from
    /// being written back to the cache after the invalidation cleared it.
    /// </summary>
    private long _generation;
    public long Generation => Volatile.Read(ref _generation);

    // Replacing the roles-to-combos mapping with a thread-safe dictionary of hash sets.
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<PlanetRoleMembership>> _rolesToCombos = new();

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

    public ModelListSnapshot<Channel, long>? GetChannelAccess(PlanetRoleMembership roleMembership)
    {
        _accessCache.TryGetValue(roleMembership, out var access);
        return access?.Snapshot;
    }

    public List<Channel> GetEmptyAccessList() => AccessListPool.Get();

    public SortedServerModelList<Channel, long> SetChannelAccess(PlanetRoleMembership roleMembership, IEnumerable<Channel> access)
    {
        SortedServerModelList<Channel, long> result;
        if (_accessCache.TryGetValue(roleMembership, out var existing))
        {
            existing.Set(access);
            result = existing;
        }
        else
        {
            var newAccess = new SortedServerModelList<Channel, long>();
            newAccess.Set(access);
            _accessCache[roleMembership] = newAccess;
            result = newAccess;
        }

        return result;
    }

    public void ClearCacheForCombo(PlanetRoleMembership roleMembership)
    {
        Interlocked.Increment(ref _generation);
        _accessCache.TryRemove(roleMembership, out _);
        _authorityCache.TryRemove(roleMembership, out _);
        _planetPermissionsCache.TryRemove(roleMembership, out _);
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForCombo(roleMembership);
    }

    public void ClearCacheForComboAndChannel(PlanetRoleMembership roleMembership, long channelId)
    {
        Interlocked.Increment(ref _generation);
        _accessCache.TryRemove(roleMembership, out _);
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForCombo(roleMembership, channelId);
    }

    public void ClearCacheForChannel(long channelId)
    {
        Interlocked.Increment(ref _generation);
        foreach (var cache in _channelPermissionCachesByType)
            cache.ClearCacheForChannel(channelId);

        ClearChannelAccessRoleComboCache(channelId);
    }

    public void ClearCacheForComboInChannel(PlanetRoleMembership roleMembership, long channelId, ChannelTypeEnum type)
    {
        Interlocked.Increment(ref _generation);
        var cache = GetChannelCache(type);
        cache.Remove(roleMembership, channelId);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        foreach (var cache in _channelPermissionCachesByType)
            cache.Clear();

        _accessCache.Clear();
        _authorityCache.Clear();
        _planetPermissionsCache.Clear();
        _rolesToCombos.Clear();
        ChannelAccessRoleComboCache.Clear();
    }

    /// <summary>
    /// Removes a specific entry from the access cache without incrementing the generation counter.
    /// Used to clean up after detecting that a stale computation raced with an invalidation.
    /// </summary>
    public void EvictAccessCacheEntry(PlanetRoleMembership roleMembership)
    {
        _accessCache.TryRemove(roleMembership, out _);
    }

    public void SetAuthority(PlanetRoleMembership roleMembership, uint authority) => _authorityCache[roleMembership] = authority;

    public uint? GetAuthority(PlanetRoleMembership roleMembership) => _authorityCache.GetValueOrDefault(roleMembership);

    public long? GetPlanetPermissions(PlanetRoleMembership roleMembership) => _planetPermissionsCache.GetValueOrDefault(roleMembership);

    public void SetPlanetPermissions(PlanetRoleMembership roleMembership, long permissions) => _planetPermissionsCache[roleMembership] = permissions;

    // NEW: Instead of manual locks, we use a thread-safe hash set.
    public void AddKnownComboToRole(long roleId, PlanetRoleMembership roleMembership)
    {
        var set = _rolesToCombos.GetOrAdd(roleId, _ => new ConcurrentHashSet<PlanetRoleMembership>());
        set.Add(roleMembership);
    }

    public List<PlanetRoleMembership>? GetCombosForRole(long roleId)
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