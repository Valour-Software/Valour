using System.Collections.Concurrent;
using Valour.Server.Utilities;

namespace Valour.Server.Services;

public class HostedPlanetService
{
    /// <summary>
    /// A cache that holds planets hosted by this node. Nodes keep their hosted
    /// planets in-memory to reduce database load.
    /// </summary>
    private readonly ModelCache<HostedPlanet> _hostedPlanets = new();
    
    private readonly HashSet<long> _hostedPlanetIds = new();
    
    public HostedPlanet Get(long id)
    {
        _hostedPlanets.Lookup.TryGetValue(id, out var planet);
        return planet;
    }
    
    public async Task Add(Planet planet)
    {
        var hosted = new HostedPlanet(planet);
        _hostedPlanets.Add(hosted);
        _hostedPlanetIds.Add(planet.Id);
    }
    
    public async Task Remove(long id)
    {
        _hostedPlanets.Remove(id);
        _hostedPlanetIds.Remove(id);
    }
    
    public IEnumerable<long> GetHostedPlanetIds()
    {
        return _hostedPlanetIds;
    }
    
}