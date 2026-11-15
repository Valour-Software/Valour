using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetTag : ClientModel<PlanetTag, long>, ISharedPlanetTag
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
    public bool Curated { get; set; }

    protected override ModelStore<PlanetTag, long> CacheStore => Client.Cache.Tags;

    public override PlanetTag Sync(ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return AddToCache(flags);
    }
}