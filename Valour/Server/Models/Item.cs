using Valour.Server.Config;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public abstract class Item : ISharedItem
{
    /// <summary>
    /// The id of this item
    /// </summary>
    public long Id { get; set; }
}

