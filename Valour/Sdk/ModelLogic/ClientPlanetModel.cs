using Valour.Sdk.Client;
using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic;

public abstract class ClientPlanetModel<TSelf, TId> : ClientModel<TSelf, TId>
    where TSelf : ClientPlanetModel<TSelf, TId>
    where TId : IEquatable<TId>
{
    public override Node Node => GetNodeForPlanet(GetPlanetId());
    
    public static Node GetNodeForPlanet(long? planetId)
    {
        if (planetId is null || planetId == -1)
            return ValourClient.PrimaryNode;
        
        return NodeManager.GetKnownByPlanet(planetId.Value);
    }

    public abstract long? GetPlanetId();

    /// <summary>
    /// Returns the planet for this model
    /// </summary>
    public ValueTask<Planet> GetPlanetAsync(bool refresh = false)
    {
        var planetId = GetPlanetId();
        
        if (planetId is null || planetId == -1)
            return ValueTask.FromResult<Planet>(null);
        
        return Planet.FindAsync(planetId.Value, refresh);
    }


}


