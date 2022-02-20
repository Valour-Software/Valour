using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Database.Items;

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
}
