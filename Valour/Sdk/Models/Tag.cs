using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class Tag : ClientModel<Tag, long>
{
    public long Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
    
    [JsonIgnore]
    [IgnoreRealtimeChanges]
    public ValourClient Client { get; private set; }
    
    public override Tag AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Client.Cache.Tags.Put(this, flags);
    }

    public override Tag RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.Tags.Remove(this, skipEvents);
    }
    
    public override Tag Sync(ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return AddToCache(flags);
    }
}