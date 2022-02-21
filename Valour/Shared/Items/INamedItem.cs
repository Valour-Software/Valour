using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

public interface INamedItem
{
    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }
}