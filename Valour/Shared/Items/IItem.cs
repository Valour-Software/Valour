using System.Text.Json;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items
{
    /// <summary>
    /// Enum for all Valour item types
    /// </summary>
    public enum ItemType
    {
        Channel,
        Category,
        Planet
    }

    /// <summary>
    /// Common class for Valour API items
    /// </summary>
    public class IItem
    {
        [JsonInclude]
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonInclude]
        [JsonPropertyName("ItemType")]
        public ItemType ItemType { get; }
    }
}
