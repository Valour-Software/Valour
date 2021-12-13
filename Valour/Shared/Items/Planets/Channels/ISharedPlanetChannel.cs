using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel
{
    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }
}
