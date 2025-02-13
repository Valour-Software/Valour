#nullable enable

using System.Collections.Concurrent;
using System.Collections.Immutable;
using StackExchange.Redis;
using Valour.Server.Utilities;
using Valour.Shared.Extensions;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node
/// </summary>
public class HostedPlanet : ServerModel<long>
{
    private readonly SortedServerModelList<Channel, long> _channels = new();
    private readonly SortedServerModelList<PlanetRole, long> _roles = new();
    public readonly PlanetPermissionsCache PermissionCache = new();

    private Channel _defaultChannel;
    private PlanetRole _defaultRole;
    
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
    
    public ModelListSnapshot<Channel, long> Channels
        => _channels.Snapshot;
    
    public Channel? GetChannel(long id)
        => _channels.Get(id);
    
    public Channel GetDefaultChannel()
        => _defaultChannel;
    
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
        // Get existing channel
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
            // this may seem weird but the props are carried to the existing channel
            // so we can use the same object reference
            _defaultChannel = result;
        }
        
        // TODO: Update permissions cache
    }
    
    public void RemoveChannel(long id)
    {
        _channels.Remove(id);
    }
    
    // Roles //
    
    public PlanetRole? GetRole(long id)
        => _roles.Get(id);
    
    public PlanetRole GetDefaultRole()
        => _defaultRole;
    
    public void SetRoles(List<PlanetRole> roles)
    {
        _roles.Set(roles);
        
        // Set default role
        foreach (var role in roles)
        {
            if (role.IsDefault)
            {
                _defaultRole = role;
                break;
            }
        }
    }
    
    public void UpsertRole(PlanetRole role)
    {
        var result = _roles.Upsert(role);
        
        if (result.IsDefault)
        {
            _defaultRole = result;
        }
    }
    
    public void RemoveRole(long id)
    {
        _roles.Remove(id);
    }

    public ModelListSnapshot<PlanetRole, long> Roles
        => _roles.Snapshot;
}