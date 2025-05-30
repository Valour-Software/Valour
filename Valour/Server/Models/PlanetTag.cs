using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetTag :ServerModel<long>, ISharedPlanetTag
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
}