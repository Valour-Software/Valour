using System.Text.Json.Serialization;
using Valour.Api.Items.Authorization;
using Valour.Shared.Items;

namespace Valour.Api.Items.Planets.Channels;

[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategoryChannel), typeDiscriminator: nameof(PlanetCategoryChannel))]
public abstract class PlanetChannel : PlanetItem
{
    public int Position { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public abstract string GetHumanReadableName();
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool force_refresh = false);
}

