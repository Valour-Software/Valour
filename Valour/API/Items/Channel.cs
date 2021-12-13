using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Api.Items;

public abstract class Channel<T> : NamedItem<T>, ISharedChannel where T : Item<T>
{
    [JsonInclude]
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonInclude]
    [JsonPropertyName("Parent_Id")]
    public ulong? Parent_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Description")]
    public string Description { get; set; }
}

