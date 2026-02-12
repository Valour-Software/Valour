#nullable enable

using System.Collections.Concurrent;
using System.Threading;
using Valour.Server.Utilities;
using Valour.Shared.Extensions;
using Valour.Shared.Utilities;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node.
/// </summary>
public class HostedPlanet : ServerModel<long>
{
    private readonly SortedServerModelList<Channel, long> _channels = new();
    private readonly SortedServerModelList<PlanetRole, long> _roles = new();

    // Fixed-size array for local-to-global role mapping.
    private readonly long[] _localToGlobalRoleId = new long[256];
    private volatile long[]? _localToGlobalRoleIdSnapshot;
    private volatile bool _isLocalToGlobalRoleIdDirty = true;

    // Lock for controlling access to the local-to-global array and snapshot.
    private readonly ReaderWriterLockSlim _localToGlobalRoleLock =
        new(LockRecursionPolicy.SupportsRecursion);

    public readonly PlanetPermissionsCache PermissionCache = new();

    // Channel permission inheritance maps (per-planet to avoid memory leaks)
    private readonly ConcurrentDictionary<long, long?> _inheritanceMap = new();
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _inheritanceLists = new();

    private Channel _defaultChannel;
    private PlanetRole _defaultRole;
    
    // Planet lock (using your simple lock implementation)
    private readonly Lock _lock = new();
    
    public Planet Planet { get; }
    
    public HostedPlanet(Planet planet, List<Channel> channels, List<PlanetRole> roles)
    {
        Planet = planet;
        Id = planet.Id;
        SetChannels(channels);
        SetRoles(roles);
    }
    
    public void Update(Planet updated)
    {
        lock (_lock)
        {
            updated.CopyAllTo(Planet);
        }
    }
    
    // Channels //
    
    public ModelListSnapshot<Channel, long> Channels => _channels.Snapshot;
    
    public Channel? GetChannel(long id) => _channels.Get(id);
    
    public Channel GetDefaultChannel() => _defaultChannel;
    
    public void SetChannels(List<Channel> channels)
    {
        _channels.Set(channels);
        // Set default channel
        foreach (var channel in channels)
        {
            if (channel.IsDefault)
            {
                _defaultChannel = channel;
                break;
            }
        }
    }
    
    public void UpsertChannel(Channel updated)
    {
        var existing = _channels.Get(updated.Id);
        var oldPermValue = existing?.InheritsPerms ?? false;
        var newPermValue = updated.InheritsPerms;
        
        if (oldPermValue != newPermValue)
        {
            PermissionCache.ClearCacheForChannel(updated.Id);
        }
        
        var result = _channels.Upsert(updated);
        if (result.IsDefault)
        {
            _defaultChannel = result;
        }
    }
    
    public void RemoveChannel(long id)
    {
        _channels.Remove(id);
    }
    
    public void SortChannels()
    {
        _channels.Sort();
    }
    
    // Roles //

    public PlanetRole? GetRoleById(long id) => _roles.Get(id);
    
    /// <summary>
    /// Returns the global role ID for a given local role id using a snapshot for fast access.
    /// If the snapshot is outdated (dirty), it is rebuilt under a write lock.
    /// </summary>
    public long GetRoleIdByIndex(int localId)
    {
        if (_isLocalToGlobalRoleIdDirty)
        {
            _localToGlobalRoleLock.EnterWriteLock();
            try
            {
                if (_isLocalToGlobalRoleIdDirty)
                {
                    // Create a fresh snapshot of the fixed-size array.
                    _localToGlobalRoleIdSnapshot = (long[])_localToGlobalRoleId.Clone();
                    _isLocalToGlobalRoleIdDirty = false;
                }
            }
            finally
            {
                _localToGlobalRoleLock.ExitWriteLock();
            }
        }
        
        _localToGlobalRoleLock.EnterReadLock();
        try
        {
            if (_localToGlobalRoleIdSnapshot is null ||
                localId < 0 || localId >= _localToGlobalRoleIdSnapshot.Length)
            {
                return 0;
            }
            return _localToGlobalRoleIdSnapshot[localId];
        }
        finally
        {
            _localToGlobalRoleLock.ExitReadLock();
        }
    }
    
    public PlanetRole? GetRoleByIndex(int index)
    {
        long globalId = GetRoleIdByIndex(index);
        return globalId == 0 ? null : _roles.Get(globalId);
    }
    
    public PlanetRole GetDefaultRole() => _defaultRole;
    
    public void SetRoles(List<PlanetRole> roles)
    {
        _roles.Set(roles);
        // Update default role and mapping under the write lock.
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            foreach (var role in roles)
            {
                if (role.IsDefault)
                {
                    _defaultRole = role;
                }
                // Ensure local role ID is within the fixed array range.
                if (role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
                {
                    _localToGlobalRoleId[role.FlagBitIndex] = role.Id;
                }
            }
            _isLocalToGlobalRoleIdDirty = true; // Mark snapshot as stale.
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
    }
    
    public void UpsertRole(PlanetRole role)
    {
        var result = _roles.Upsert(role);
        if (result.IsDefault)
        {
            _defaultRole = result;
        }
        
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            if (role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
            {
                _localToGlobalRoleId[role.FlagBitIndex] = role.Id;
                _isLocalToGlobalRoleIdDirty = true;
            }
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
    }
    
    public void RemoveRole(long id)
    {
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            var role = _roles.Get(id);
            if (role != null && role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
            {
                // Reset the mapping for this local role id.
                _localToGlobalRoleId[role.FlagBitIndex] = 0;
            }
            _isLocalToGlobalRoleIdDirty = true;
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
        _roles.Remove(id);
    }

    public ModelListSnapshot<PlanetRole, long> Roles => _roles.Snapshot;

    // Channel Inheritance //

    /// <summary>
    /// Gets the inherited-from channel ID for a given channel, or null if not cached.
    /// </summary>
    public bool TryGetInheritanceTarget(long channelId, out long? targetId) =>
        _inheritanceMap.TryGetValue(channelId, out targetId);

    /// <summary>
    /// Sets the inheritance target for a channel.
    /// </summary>
    public void SetInheritanceTarget(long channelId, long targetChannelId)
    {
        _inheritanceMap[channelId] = targetChannelId;

        // Track the inverse relationship
        var inheritorsList = _inheritanceLists.GetOrAdd(targetChannelId, _ => new ConcurrentHashSet<long>());
        inheritorsList.Add(channelId);
    }

    /// <summary>
    /// Gets all channels that inherit from a given channel.
    /// </summary>
    public ConcurrentHashSet<long>? GetInheritors(long channelId)
    {
        _inheritanceLists.TryGetValue(channelId, out var result);
        return result;
    }

    /// <summary>
    /// Clears inheritance cache for a channel.
    /// </summary>
    public void ClearInheritanceCache(long channelId)
    {
        _inheritanceMap.TryRemove(channelId, out _);
        if (_inheritanceLists.TryGetValue(channelId, out var inheritors))
        {
            inheritors.Clear();
        }
    }

    /// <summary>
    /// Clears all channel inheritance cache state for this planet.
    /// </summary>
    public void ClearAllInheritanceCaches()
    {
        _inheritanceMap.Clear();
        _inheritanceLists.Clear();
    }
}
