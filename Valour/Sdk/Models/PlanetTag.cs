using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetTag : ClientModel<PlanetTag, long>, ISharedPlanetTag
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
    
    public override PlanetTag AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Client.Cache.Tags.Put(this, flags);
    }

    public override PlanetTag RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.Tags.Remove(this, skipEvents);
    }
    
    public override PlanetTag Sync(ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return AddToCache(flags);
    }
}