using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using System.Text.Json.Serialization;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class TenorFavorite : ClientModel<TenorFavorite, long>, ISharedTenorFavorite
{
    public override string BaseRoute =>
            ISharedTenorFavorite.BaseRoute;

    [JsonIgnore]
    public override Node Node => Client?.AccountNode;

    /// <summary>
    /// The Tenor Id of this favorite
    /// </summary>
    public string TenorId { get; set; }

    /// <summary>
    /// The Id of the user this favorite belongs to
    /// </summary>
    public long UserId { get; set; }
    
    [JsonConstructor]
    private TenorFavorite(): base() {}
    public TenorFavorite(ValourClient client) : base(client) { }

    public override TenorFavorite AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override TenorFavorite RemoveFromCache(bool skipEvents)
    {
        return this;
    }
}
