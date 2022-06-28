using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

public interface IPlanetChannel
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonInclude]
    [JsonPropertyName("ParentId")]
    public ulong? ParentId { get; set; }

    [JsonInclude]
    [JsonPropertyName("PlanetId")]
    public ulong PlanetId { get; set; }

    [JsonInclude]
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("Description")]
    public string Description { get; set; }

    public ItemType ItemType { get; }

    public abstract Task<TaskResult> SetDescriptionAsync(string desc);
    public abstract Task<TaskResult> SetNameAsync(string name);
    public abstract Task<TaskResult> SetParentIdAsync(ulong? planetId);
    public abstract Task<TaskResult> DeleteAsync();
    public abstract string GetItemTypeName();
    public abstract Task<Planet> GetPlanetAsync();
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false);
}

