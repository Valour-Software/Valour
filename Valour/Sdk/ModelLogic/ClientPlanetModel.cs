using Valour.Sdk.Client;
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
            if (_planet is not null)
                return _planet;
            
            var planetId = GetPlanetId();
            return planetId is null || planetId == -1
                ? null
                : _planet ??= Planet.Cache.Get(planetId.Value);
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


