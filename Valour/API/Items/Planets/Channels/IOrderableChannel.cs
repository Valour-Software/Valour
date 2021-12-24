using System.Text.Json.Serialization;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items.Planets.Channels;

public interface IOrderableChannel
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonInclude]
    [JsonPropertyName("Parent_Id")]
    public ulong? Parent_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("Description")]
    public string Description { get; set; }

    public ItemType ItemType { get; }

    public abstract string GetItemTypeName();

    public Task<Planet> GetPlanetAsync();

    public Task<TaskResult> DeleteAsync();

    public Task<TaskResult> SetNameAsync(string name);

    public Task<TaskResult> SetDescriptionAsync(string description);
}

