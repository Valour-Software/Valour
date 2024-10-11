using Valour.Shared.Models;

namespace Valour.Server.Models;

public abstract class ServerModel : ISharedModel, IHasId
{
    /// <summary>
    /// The id of this item
    /// </summary>
    public long Id { get; set; }

    object IHasId.Id => Id;
}

