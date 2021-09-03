using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Items
{
    /// <summary>
    /// Common functionality for named items
    /// </summary>
    

    public class INamedItem : IItem
    {
        [JsonInclude]
        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }
}
