using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Api.Items;

public abstract class NamedItem<T> : Item<T>, ISharedNamedItem where T : Item<T>
{
    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }
}

