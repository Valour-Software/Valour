using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

/// <summary>
/// Common functionality between channels
/// </summary>
public class Channel : NamedItem
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

    public override ItemType ItemType => throw new NotImplementedException();
}

