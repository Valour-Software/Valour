#nullable enable

using System.Threading;
using Valour.Server.Utilities;
using Valour.Shared.Extensions;

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
        // TODO: Update permissions cache
    }
    
    public void RemoveChannel(long id)
    {
        _channels.Remove(id);
    }
    
    // Roles //

    public PlanetRole? GetRoleByGlobalId(long id) => _roles.Get(id);
    
    /// <summary>
    /// Returns the global role ID for a given local role id using a snapshot for fast access.
    /// If the snapshot is outdated (dirty), it is rebuilt under a write lock.
    /// </summary>
    public long GetGlobalRoleIdByLocalId(int localId)
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
    
    public PlanetRole? GetRoleByLocalId(int localId)
    {
        long globalId = GetGlobalRoleIdByLocalId(localId);
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
                if (role.LocalId >= 0 && role.LocalId < _localToGlobalRoleId.Length)
                {
                    _localToGlobalRoleId[role.LocalId] = role.Id;
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
            if (role.LocalId >= 0 && role.LocalId < _localToGlobalRoleId.Length)
            {
                _localToGlobalRoleId[role.LocalId] = role.Id;
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
            if (role != null && role.LocalId >= 0 && role.LocalId < _localToGlobalRoleId.Length)
            {
                // Reset the mapping for this local role id.
                _localToGlobalRoleId[role.LocalId] = 0;
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
}
