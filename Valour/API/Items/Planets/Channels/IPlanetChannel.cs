using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;

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

    public abstract Task<Planet> GetPlanetAsync();
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false);
}

