using System.Text.Json.Serialization;
using Valour.Shared.Items.Planets;

namespace Valour.Shared.Items;

/// <summary>
/// Common functionality between channels
/// </summary>
public abstract class Channel : ISharedItem, IPlanetItem, INamed
{
    [JsonInclude]
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonInclude]
    [JsonPropertyName("ParentId")]
    public ulong? ParentId { get; set; }

    [JsonInclude]
    [JsonPropertyName("Description")]
    public string Description { get; set; }

    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("PlanetId")]
    public ulong PlanetId { get; set; }

    public override ItemType ItemType => throw new NotImplementedException();
}

