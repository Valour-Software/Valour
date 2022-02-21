using System.Text.Json.Serialization;
using Valour.Shared.Items.Planets;

namespace Valour.Shared.Items;

/// <summary>
/// Common functionality between channels
/// </summary>
public abstract class Channel : Item, IPlanetItem, INamedItem
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

    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    public override ItemType ItemType => throw new NotImplementedException();
}

