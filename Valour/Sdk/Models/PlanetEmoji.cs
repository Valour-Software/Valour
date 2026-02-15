using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetEmoji : ClientPlanetModel<PlanetEmoji, long>, ISharedPlanetEmoji
{
    public override string BaseRoute =>
        ISharedPlanetEmoji.GetBaseRoute(PlanetId);

    public override string IdRoute =>
        ISharedPlanetEmoji.GetIdRoute(PlanetId, Id);

    public long PlanetId { get; set; }
    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    protected override long? GetPlanetId()
        => PlanetId;

    [JsonConstructor]
    private PlanetEmoji() : base() {}
    public PlanetEmoji(ValourClient client) : base(client) {}

    public override PlanetEmoji AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.Emojis.Put(this, flags);
    }

    public override PlanetEmoji RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Emojis.Remove(this, skipEvents);
    }
}
