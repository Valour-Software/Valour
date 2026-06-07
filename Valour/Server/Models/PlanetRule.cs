using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetRule : ServerModel<long>, ISharedPlanetRule
{
    public long PlanetId { get; set; }
    public uint Position { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
}
