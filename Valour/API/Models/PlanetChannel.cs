using System.Text.Json.Serialization;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
public abstract class PlanetChannel : Channel, IPlanetItem
{
    #region IPlanetItem implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetItem.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/planetchannels";

    #endregion

    public int Position { get; set; }
    public long? ParentId { get; set; }

    public abstract string GetHumanReadableName();
    public abstract Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool force_refresh = false);

    public async ValueTask<PlanetChannel> GetParentAsync()
    {
        if (ParentId is null)
        {
            return null;
        }
        return await PlanetCategory.FindAsync(ParentId.Value, PlanetId);
    }

    public abstract Task<bool> HasPermissionAsync(PlanetMember member, Permission perm);
}

