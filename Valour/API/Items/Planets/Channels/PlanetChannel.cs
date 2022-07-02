using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;
using Valour.Shared.Items;

namespace Valour.Api.Items.Planets.Channels;

[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategoryChannel), typeDiscriminator: nameof(PlanetCategoryChannel))]
public abstract class PlanetChannel<T> : SyncedItem<T> where T : class, ISharedItem
{
    public int Position { get; set; }
    public ulong? ParentId { get; set; }
    public ulong PlanetId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public abstract string GetHumanReadableName();
    public abstract Task<Planet> GetPlanetAsync(bool refresh = false);
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false);
}

