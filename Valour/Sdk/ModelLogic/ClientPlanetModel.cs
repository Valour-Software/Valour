using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic.Exceptions;
using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic;

public abstract class ClientPlanetModel<TSelf, TId> : ClientModel<TSelf, TId>
    where TSelf : ClientPlanetModel<TSelf, TId>
    where TId : IEquatable<TId>
{
    private Planet _planet;

    /// <summary>
    /// The Planet this model belongs to.
    /// The Planet should always be in cache before Planet Models are grabbed.
    /// If for some reason planet is null, it will be fetched from the cache.
    /// If it is not in cache, you should have loaded the planet first.
    /// </summary>
    public Planet Planet
    {
        get
        {
            if (_planet is not null) // If the planet is already stored, return it
                return _planet;
            
            var planetId = GetPlanetId(); // Get the planet id from the model
            if (planetId is null || planetId == -1) // If the id is null or -1, the model is not associated with a planet
                return null;
                    
            _planet ??= Planet.Cache.Get(planetId.Value); // Try to assign from cache
            
            if (_planet is null) // If it wasn't in cache, throw an exception. Should have loaded the planet first.
                throw new PlanetNotLoadedException(planetId.Value, this);
            
            // Return the planet
            return _planet;
        }
    }
    
    public override Node Node => GetNodeForPlanet(GetPlanetId());
    
    public static Node GetNodeForPlanet(long? planetId)
    {
        if (planetId is null || planetId == -1)
            return ValourClient.PrimaryNode;
        
        return NodeManager.GetKnownByPlanet(planetId.Value);
    }

    public abstract long? GetPlanetId();
}


