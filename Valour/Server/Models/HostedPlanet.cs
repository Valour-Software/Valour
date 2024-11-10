using Valour.Server.Utilities;
using Valour.Shared.Extensions;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node
/// </summary>
public class HostedPlanet : IHasId
{
    public Planet Planet { get; private set; }
    
    public SortedModelCache<PlanetRole> Roles { get; private set; }
    
    object IHasId.Id => Planet.Id;
    
    public HostedPlanet(Planet planet)
    {
        Planet = planet;
    }
    
    public void Update(Planet updated)
    {
        Planet.CopyAllTo(updated);
    }
    
    
}