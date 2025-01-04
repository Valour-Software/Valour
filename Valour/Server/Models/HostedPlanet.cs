#nullable enable

using Valour.Server.Utilities;
using Valour.Shared.Extensions;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node
/// </summary>
public class HostedPlanet : ServerModel<long>
{
    private SortedServerModelList<Channel, long> _channels = new();
    private SortedServerModelList<PlanetRole, long> _roles = new();
    public PlanetPermissionsCache PermissionCache = new();
    
    // Planet lock
    private readonly Lock _lock = new();
    
    public Planet Planet { get; }
    
    public long Id
    {
        get => Planet.Id;
        set => Planet.Id = value;
    }
    
    public HostedPlanet(Planet planet)
    {
        Planet = planet;
    }
    
    public void Update(Planet updated)
    {
        lock (_lock)
        {
            Planet.CopyAllTo(updated);
        }
    }
    
    public Channel? GetChannel(long id)
        => _channels.Get(id);
    
    public PlanetRole? GetRole(long id)
        => _roles.Get(id);
    
    public void SetChannels(List<Channel> channels)
    {
        _channels.Set(channels);
    }
    
    public void SetRoles(List<PlanetRole> roles)
    {
        _roles.Set(roles);
    }
    
    public void UpsertChannel(Channel channel)
    {
        _channels.Upsert(channel);
    }
    
    public void UpsertRole(PlanetRole role)
    {
        _roles.Upsert(role);
    }
    
    public void RemoveChannel(long id)
    {
        _channels.Remove(id);
    }
    
    public void RemoveRole(long id)
    {
        _roles.Remove(id);
    }
}