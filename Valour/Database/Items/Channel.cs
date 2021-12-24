using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Database.Items;

public abstract class Channel : NamedItem, ISharedChannel
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

