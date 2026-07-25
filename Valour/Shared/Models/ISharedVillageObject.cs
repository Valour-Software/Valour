namespace Valour.Shared.Models;

/// <summary>
/// A placed prop: furniture, decor, or an interactable. Objects are stored
/// separately from chunk tile data because they are individually owned,
/// moved, and depth-sorted against characters at render time.
/// </summary>
public interface ISharedVillageObject : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/villageobjects";

    long MapId { get; set; }

    /// <summary>
    /// Logical sprite key into the map's tileset
    /// </summary>
    string DefinitionKey { get; set; }

    int X { get; set; }

    int Y { get; set; }

    /// <summary>
    /// Rotation in 90 degree steps
    /// </summary>
    int Rotation { get; set; }

    /// <summary>
    /// Tie-breaker for objects sharing a tile row
    /// </summary>
    int ZIndex { get; set; }

    bool BlocksMovement { get; set; }

    long? OwnerMemberId { get; set; }
}
