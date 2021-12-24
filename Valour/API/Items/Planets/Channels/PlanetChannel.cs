using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

public abstract class PlanetChannel<T> : Channel<T>, ISharedPlanetChannel, IOrderableChannel where T : Item<T>
{
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    public abstract Task<TaskResult> SetDescriptionAsync(string desc);
    public abstract Task<TaskResult> SetNameAsync(string name);
    public abstract Task<TaskResult> SetParentIdAsync(ulong? planet_id);
    public abstract Task<TaskResult> DeleteAsync();
    public abstract string GetItemTypeName();
    public abstract Task<Planet> GetPlanetAsync();
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(ulong role_id, bool force_refresh = false);
}

