using Valour.Server.Utilities;
using Valour.Shared.Extensions;
using Valour.Shared.Models;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node
/// </summary>
public class HostedPlanet : ServerModel<long>
{
    public Planet Planet { get; private set; }
    public SortedServerModelCache<Channel, long> Channels = new();
    public SortedServerModelCache<PlanetRole, long> Roles = new();
    
    public long Id
    {
        get => Planet.Id;
        set => Planet.Id = value;
    }
    
    public HostedPlanet(Planet planet)
    {
        Planet = planet;
    }
    
    public void Update(Planet updated)
    {
        Planet.CopyAllTo(updated);
    }
}