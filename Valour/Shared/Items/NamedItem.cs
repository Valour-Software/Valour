using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public abstract class NamedItem : Item
{
    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }
}