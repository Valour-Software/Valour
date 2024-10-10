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
    
    public HostedPlanet Get(long id)
    {
        _hostedPlanets.Lookup.TryGetValue(id, out var planet);
        return planet;
    }
    
    public void Add(HostedPlanet planet)
    {
        _hostedPlanets.Add(planet);
    }
    
}