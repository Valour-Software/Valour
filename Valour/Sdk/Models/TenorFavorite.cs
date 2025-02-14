using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class TenorFavorite : ClientModel<TenorFavorite, long>, ISharedTenorFavorite
{
    public override string BaseRoute =>
            ISharedTenorFavorite.BaseRoute;

    /// <summary>
    /// The Tenor Id of this favorite
    /// </summary>
    public string TenorId { get; set; }

    /// <summary>
    /// The Id of the user this favorite belongs to
    /// </summary>
    public long UserId { get; set; }

    public override TenorFavorite AddToCache()
    {
        return this;
    }

    public override TenorFavorite TakeAndRemoveFromCache()
    {
        return this;
    }
}