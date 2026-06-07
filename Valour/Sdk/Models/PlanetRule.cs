using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetRule : ClientPlanetModel<PlanetRule, long>, ISharedPlanetRule
{
    public override string BaseRoute => ISharedPlanetRule.GetBaseRoute(PlanetId);

    public override string IdRoute => ISharedPlanetRule.GetIdRoute(PlanetId, Id);

    public long PlanetId { get; set; }
    public uint Position { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private PlanetRule() : base()
    {
    }

    public PlanetRule(ValourClient client) : base(client)
    {
    }

    public override PlanetRule AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.Rules.Put(this, flags);
    }

    public override PlanetRule RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Rules.Remove(this, skipEvents);
    }
}
