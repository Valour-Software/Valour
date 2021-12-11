using System.Text.Json.Serialization;


namespace Valour.Shared.Items.Planets.Channels
{
    public abstract class PlanetChannel<T> : NamedItem<T> where T : Item<T>
    {
        [JsonInclude]
        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        [JsonInclude]
        [JsonPropertyName("Parent_Id")]
        public ulong? Parent_Id { get; set;}

        [JsonInclude]
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        [JsonInclude]
        [JsonPropertyName("Description")]
        public string Description { get; set; }
    }
}