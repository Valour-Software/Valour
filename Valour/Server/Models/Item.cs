using Valour.Api.Models;
using Valour.Server.Config;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public abstract class Item : ISharedItem
{
    /// <summary>
    /// The id of this item
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Planet items belong to a specific node
    /// </summary>
    public string NodeName { get; set; } = NodeConfig.Instance.Name;
}

