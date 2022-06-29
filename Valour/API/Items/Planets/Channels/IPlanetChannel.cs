using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;

namespace Valour.Api.Items.Planets.Channels;

public interface IPlanetChannel
{
    public ulong Id { get; set; }
    public int Position { get; set; }
    public ulong? ParentId { get; set; }
    public ulong PlanetId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public string GetHumanReadableName();
    public abstract Task<Planet> GetPlanetAsync(bool refresh = false);
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false);
}

