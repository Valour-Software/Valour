using System.Collections.Concurrent;
using Valour.Server.Exceptions;
using Valour.Server.Utilities;

namespace Valour.Server.Services;

public struct HostedPlanetResult
{
    public HostedPlanet HostedPlanet;
    public string CorrectNode;

    // If found, return the planet
    public HostedPlanetResult(HostedPlanet hostedPlanet)
    {
        this.HostedPlanet = hostedPlanet;
        this.CorrectNode = null;
    }
    
    // If not found, return the correct node
    public HostedPlanetResult(string correctNode)
    {
        this.HostedPlanet = null;
        this.CorrectNode = correctNode;
    }
}

public class HostedPlanetService
{
    private readonly ValourDb _db;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ModelCacheService _cache;
    
    public HostedPlanetService(ValourDb db, NodeLifecycleService nodeLifecycleService, ModelCacheService cache)
    {
        _db = db;
        _nodeLifecycleService = nodeLifecycleService;
        _cache = cache;
    }
    
    public async Task<HostedPlanet> BeginHosting(long planetId)
    {
        // If we're already hosting this planet, do nothing
        var cached = _cache.HostedPlanets.Get(planetId);
        if (cached is not null)
            return cached;
        
        var planet = (await _db.Planets.FindAsync(planetId)).ToModel();
        if (planet == null)
            return null;
        
        var hostedPlanet = new HostedPlanet(planet);
        _cache.HostedPlanets.Set(hostedPlanet);

        // Load data that should be cached
        var channels = await _db.Channels
            .Where(c => c.PlanetId == planetId)
            .Select(c => c.ToModel())
            .ToListAsync();
        
        hostedPlanet.SetChannels(channels);
        
        var roles = await _db.PlanetRoles
            .Where(r => r.PlanetId == planetId)
            .Select(r => r.ToModel())
            .ToListAsync();
        
        hostedPlanet.SetRoles(roles);

        return hostedPlanet;
    }
    
    /// <summary>
    /// Returns the hosted planet if it is hosted on this node, or the correct node if it is not.
    /// </summary>
    public async ValueTask<HostedPlanetResult> TryGetAsync(long id)
    {
        var hostedPlanet = _cache.HostedPlanets.Get(id);
        if (hostedPlanet is not null)
            return new HostedPlanetResult(hostedPlanet);

        // Determine if the planet is hosted elsewhere
        var nodeName = await _nodeLifecycleService.GetActiveNodeForPlanetAsync(id);
        
        // If it's here, we should be hosting it
        if (nodeName == _nodeLifecycleService.Name)
        {
            return new HostedPlanetResult(await BeginHosting(id));
        }
        
        // Return where it should be hosted
        return new HostedPlanetResult(nodeName);
    }
    
    /// <summary>
    /// Returns the given hosted planet if hosted on this node.
    /// Otherwise, throws an exception which will be automatically handled by the API
    /// to redirect to the correct node. See: <see cref="NotHostedExceptionFilter"/>
    /// </summary>
    public async Task<HostedPlanet> GetRequiredAsync(long id)
    {
        var result = await TryGetAsync(id);
        if (result.HostedPlanet is not null)
        {
            return result.HostedPlanet;
        }

        throw new PlanetNotHostedException(id, result.CorrectNode);
    }
    
    public void Remove(long id)
    {
        _cache.HostedPlanets.Remove(id);
    }
    
    public bool IsHosted(long id)
    {
        return _cache.HostedPlanets.ContainsKey(id);
    }
    
    public long[] GetHostedIds()
    {
        return _cache.HostedPlanets.Ids;
    }
    
}