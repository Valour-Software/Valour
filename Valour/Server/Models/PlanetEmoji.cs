using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetEmoji : ServerModel<long>, ISharedPlanetEmoji
{
    public long PlanetId { get; set; }
    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
