using Valour.Shared.Models;

namespace Valour.Server.Models;

public class VillageObject : ServerModel<long>, ISharedVillageObject
{
    /// <summary>
    /// The id of the planet this object belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The map this object is placed on
    /// </summary>
    public long MapId { get; set; }

    /// <summary>
    /// Logical sprite key into the map's tileset
    /// </summary>
    public string DefinitionKey { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>
    /// Rotation in 90 degree steps
    /// </summary>
    public int Rotation { get; set; }

    /// <summary>
    /// Tie-breaker for objects sharing a tile row
    /// </summary>
    public int ZIndex { get; set; }

    public bool BlocksMovement { get; set; }

    public long? OwnerMemberId { get; set; }
}
