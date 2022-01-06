using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public class PlanetChannel : Channel
{
    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }
}
