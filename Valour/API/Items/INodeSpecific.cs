using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Api.Items;

/// <summary>
/// This interface specifies that an item is designed to be returned from
/// a specific node
/// </summary>
public interface INodeSpecific
{
    /// <summary>
    /// This is the node that returned the API item.
    /// This node should be used for any API 
    /// </summary>
    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("Node")]
    public string Node { get; set; }

    [NotMapped]
    [JsonIgnore]
    /// <summary>
    /// This is the location of the node that should be used
    /// for this item's API calls.
    /// </summary>
#if DEBUG
    public string NodeLocation => $"/";
#else
    public string NodeLocation => $"https://{Node}.nodes.valour.gg";
#endif

}
