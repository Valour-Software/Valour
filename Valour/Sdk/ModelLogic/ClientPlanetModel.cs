using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic.Exceptions;
using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic;

public abstract class ClientPlanetModel<TSelf, TId> : ClientModel<TSelf, TId>
    where TSelf : ClientPlanetModel<TSelf, TId>
    where TId : IEquatable<TId>
{
    private Planet _planet;    
    protected abstract long? GetPlanetId();
    
    protected ClientPlanetModel() : base() {}
    public ClientPlanetModel(ValourClient client) : base(client) { }

    /// <summary>
    /// The Planet this model belongs to.
    /// The Planet should always be in cache before Planet Models are grabbed.
    /// If for some reason planet is null, it will be fetched from the cache.
    /// If it is not in cache, you should have loaded the planet first.
    /// </summary>
    [JsonIgnore]
    public Planet Planet
    {
        get
        {
            return GetPlanet(true);
        }
    }
    
    public Planet GetPlanet(bool throwIfNull = true)
    {
        if (_planet is not null) // If the planet is already stored, return it
            return _planet;
            
        var planetId = GetPlanetId(); // Get the planet id from the model
        if (planetId is null || planetId == -1) // If the id is null or -1, the model is not associated with a planet
            return null;
            
        Client.Cache.Planets.TryGet(planetId.Value, out _planet); // Try to assign from cache
        if (_planet is null  && throwIfNull) // If it wasn't in cache, throw an exception. Should have loaded the planet first.
            throw new PlanetNotLoadedException(planetId.Value, this);
            
        // Return the planet
        return _planet;
    }

    [JsonIgnore]
    public override Node Node
    {
        get
        {
            var planetId = GetPlanetId();
            
            if (planetId is null || planetId == -1)
                return Client.PrimaryNode;
            
            return Client.NodeService.GetKnownByPlanet(planetId.Value);
        }
    }
}


