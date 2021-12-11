using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items.Planets.Channels;

public interface IPlanetChannel
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonPropertyName("Parent_Id")]
    public ulong? Parent_Id { get; set; }

    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [JsonPropertyName("Description")]
    public string Description { get; set; }

    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonPropertyName("ItemType")]
    public ItemType ItemType { get; }

    public Task<TaskResult> SetDescriptionAsync(string desc);
    public Task<TaskResult> SetNameAsync(string name);
    public Task<TaskResult> SetParentIdAsync(ulong? planet_id);
    public Task<TaskResult> DeleteAsync();
    public string GetItemTypeName();
    public Task<Planet> GetPlanetAsync();
    public Task<PermissionsNode> GetPermissionsNodeAsync(ulong role_id, bool force_refresh = false);
}

