using System.Text.Json.Serialization;

namespace Valour.Shared.Items
{
    /// <summary>
    /// Common functionality for named items
    /// </summary>
    public interface ISharedNamedItem
    {
        [JsonInclude]
        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }
}
