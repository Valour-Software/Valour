using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Shared.Items;
using Valour.Shared.Planets;

namespace Valour.Shared.Categories
{
    /// <summary>
    /// This class allows for information about item contents and ordering within a category
    /// to be easily sent to the server
    /// </summary>
    public class CategoryContentData
    {
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        [JsonPropertyName("ItemType")]
        public ItemType ItemType { get; set; }
    }
}
