using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// A placed prop: furniture, decor, or an interactable.
/// </summary>
public class VillageObject : ClientPlanetModel<VillageObject, long>, ISharedVillageObject
{
    public override string BaseRoute => ISharedVillageObject.BaseRoute;

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

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private VillageObject() : base() { }
    public VillageObject(ValourClient client) : base(client) { }

    public override VillageObject AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var planet = GetPlanet(false);
        if (planet is null)
            return this;

        return planet.VillageObjects.Put(this, flags);
    }

    public override VillageObject RemoveFromCache(bool skipEvents = false)
    {
        return Planet.VillageObjects.Remove(this, skipEvents);
    }
}
