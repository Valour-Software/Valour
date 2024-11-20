using Valour.Shared.Models;

namespace Valour.Server.Models;

public abstract class ServerModel<TId> : ISharedModel<TId>
{
    /// <summary>
    /// The id of this item
    /// </summary>
    public TId Id { get; set; }
}

