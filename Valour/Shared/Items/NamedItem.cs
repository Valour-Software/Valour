
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public abstract class NamedItem : Item
{
    [JsonPropertyName("Name")]
    public string Name { get; set; }
}