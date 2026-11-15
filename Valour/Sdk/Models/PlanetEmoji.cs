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

    protected override ModelStore<PlanetEmoji, long> CacheStore => Planet.Emojis;
}
