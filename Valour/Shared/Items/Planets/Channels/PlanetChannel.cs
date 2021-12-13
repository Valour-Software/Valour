using System.Text.Json.Serialization;


namespace Valour.Shared.Items.Planets.Channels
{
    public abstract class PlanetChannel<T> : Channel<T> where T : Item<T>
    {
        [JsonInclude]
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }
    }
}