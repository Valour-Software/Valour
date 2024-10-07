namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node
/// </summary>
public class HostedPlanet
{
    public Planet Planet { get; set; }
    
    /// <summary>
    /// Lookup table for roles by id
    /// </summary>
    public Dictionary<long, PlanetRole> RoleLookup { get; private set; }

    /// <summary>
    /// All roles. Guaranteed to be sorted by position.
    /// </summary>
    public IReadOnlyList<PlanetRole> Roles { private get; init; }
    private List<PlanetRole> _roles;
    
    public HostedPlanet(Planet planet, List<PlanetRole> roles)
    {
        Planet = planet;
        
        RoleLookup = roles.ToDictionary(x => x.Id);
        _roles = roles.OrderBy(x => x.Position).ToList();
        Roles = _roles; // cast to IReadOnlyList
    }
    
    public void AddRole(PlanetRole role)
    {
        RoleLookup.Add(role.Id, role);
        _roles.Add(role);
        _roles.Sort(PlanetRole.Orderer);
    }
    
    public void RemoveRole(long roleId)
    {
        if (RoleLookup.TryGetValue(roleId, out var role))
        {
            RoleLookup.Remove(roleId);
            _roles.Remove(role);
        }
    }
    
    public void UpdateRole(PlanetRole updated)
    {
        // we copy the properties of the updated to the existing updated
        if (RoleLookup.TryGetValue(updated.Id, out var oldRole))
        {
            _roles.Sort(PlanetRole.Orderer);
        }
    }
}