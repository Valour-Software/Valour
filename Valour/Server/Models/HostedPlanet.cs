#nullable enable

using Valour.Server.Utilities;
using Valour.Shared.Collections;
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
    
    // Use SnapshotArray for the role mapping
    private readonly SnapshotArray<long> _indexToRoleId = new(256);
    
    public readonly PlanetPermissionsCache PermissionCache = new();

    private Channel _defaultChannel = null!;
    private PlanetRole _defaultRole = null!;
    
    // Planet lock
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
    /// </summary>
    public long GetRoleIdByIndex(int localId)
    {
        // Use the safe method to avoid out-of-range exceptions
        return _indexToRoleId.GetSafe(localId);
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
        
        // First pass to find default role
        foreach (var role in roles)
        {
            if (role.IsDefault)
            {
                _defaultRole = role;
                break;
            }
        }
        
        // Update the role mapping
        _indexToRoleId.UpdateRange(array => {
            foreach (var role in roles)
            {
                if (role.FlagBitIndex >= 0 && role.FlagBitIndex < array.Length)
                {
                    array[role.FlagBitIndex] = role.Id;
                }
            }
        });
    }
    
    public void UpsertRole(PlanetRole role)
    {
        var result = _roles.Upsert(role);
        if (result.IsDefault)
        {
            _defaultRole = result;
        }
        
        if (role.FlagBitIndex >= 0 && role.FlagBitIndex < 256)
        {
            _indexToRoleId.SetSafe(role.FlagBitIndex, role.Id);
        }
    }
    
    public void RemoveRole(long id)
    {
        var role = _roles.Get(id);
        if (role != null && role.FlagBitIndex >= 0 && role.FlagBitIndex < 256)
        {
            _indexToRoleId.SetSafe(role.FlagBitIndex, 0);
        }
        
        _roles.Remove(id);
    }

    public ModelListSnapshot<PlanetRole, long> Roles => _roles.Snapshot;
}
