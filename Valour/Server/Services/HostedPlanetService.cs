using System.Collections.Concurrent;
using Valour.Server.Exceptions;
using Valour.Server.Utilities;

namespace Valour.Server.Services;

public class HostedPlanetService
{
    private readonly ValourDb _db;
    
    public HostedPlanetService(ValourDb db)
    {
        _db = db;
    }
    
    /// <summary>
    /// A cache that holds planets hosted by this node. Nodes keep their hosted
    /// planets in-memory to reduce database load.
    /// </summary>
    private readonly ServerModelCache<HostedPlanet, long> _hostedPlanets = new();

    public async Task<HostedPlanet> BeginHosting(long planetId)
    {
        // If we're already hosting this planet, do nothing
        if (_hostedPlanets.TryGet(planetId, out var hosted))
        {
            return hosted;
        }
        
        var planet = (await _db.Planets.FindAsync(planetId)).ToModel();
        if (planet == null)
            return null;
        
        hosted = new HostedPlanet(planet);
        _hostedPlanets.Upsert(hosted);

        // Load data that should be cached
        var channels = await _db.Channels
            .Where(c => c.PlanetId == planetId)
            .Select(c => c.ToModel())
            .ToListAsync();
        
        hosted.Channels.Set(channels);
        
        var roles = await _db.PlanetRoles
            .Where(r => r.PlanetId == planetId)
            .Select(r => r.ToModel())
            .ToListAsync();
        
        hosted.Roles.Set(roles);

        return hosted;
    }
    
    public HostedPlanet Get(long id)
    {
        _hostedPlanets.TryGet(id, out var planet);
        return planet;
    }
    
    public HostedPlanet GetRequired(long id)
    {
        if (_hostedPlanets.TryGet(id, out var planet))
        {
            return planet;
        }

        throw new PlanetNotHostedException(id);
    }
    
    public void Remove(long id)
    {
        _hostedPlanets.Remove(id);
    }
    
    public bool IsHosted(long id)
    {
        return _hostedPlanets.Contains(id);
    }
    
    public IEnumerable<long> GetHostedPlanetIds()
    {
        return _hostedPlanets.Ids;
    }
    
}