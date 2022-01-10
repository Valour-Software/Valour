using System.Text.Json.Serialization;
using Valour.Shared.Items;

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
