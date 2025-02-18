using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : ClientPlanetModel<PlanetRole, long>, ISharedPlanetRole
{
    public override string BaseRoute =>
        ISharedPlanetRole.GetBaseRoute(PlanetId);

    public override string IdRoute => 
        ISharedPlanetRole.GetIdRoute(PlanetId, Id);

    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    protected override long? GetPlanetId()
        => PlanetId;
    
    // Coolest role on this damn platform.
    // Fight me.
    public static PlanetRole VictorRole = new PlanetRole()
    {
        Name = "Victor Class",
        Id = 0,
        Position = int.MaxValue,
        PlanetId = 0,
        Color = "ff00ff",
        AnyoneCanMention = false,
    };

    public static PlanetRole DefaultRole = new PlanetRole()
    {
        Name = "Default",
        Id = 0,
        Position = int.MaxValue,
        PlanetId = 0,
        Color = "#ffffff",
        Permissions = PlanetPermissions.Default,
        ChatPermissions = ChatChannelPermissions.Default,
        CategoryPermissions = Valour.Shared.Authorization.CategoryPermissions.Default,
        VoicePermissions = VoiceChannelPermissions.Default,
        AnyoneCanMention = false,
        IsDefault = true
    };

    // Cached values
    private List<PermissionsNode> PermissionsNodes { get; set; }

    /// <summary>
    /// The index of the role in the membership flags.
    /// Ex: 5 would be the 5th bit in the membership flags
    /// </summary>
    public int FlagBitIndex { get; set; }
    
    /// <summary>
    /// True if this is an admin role - meaning that it overrides all permissions
    /// </summary>
    public bool IsAdmin { get; set; }
    
    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public uint Position { get; set; }
    
    /// <summary>
    /// True if this is the default (everyone) role
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public long Permissions { get; set; }

    /// <summary>
    /// The chat channel permissions for the role
    /// </summary>
    public long ChatPermissions { get; set; }

    /// <summary>
    /// The category permissions for the role
    /// </summary>
    public long CategoryPermissions { get; set; }

    /// <summary>
    /// The voice channel permissions for the role
    /// </summary>
    public long VoicePermissions { get; set; }

    /// <summary>
    /// The name of this role
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The hex color for the role
    /// </summary>
    public string Color { get; set; }

    // Formatting options
    public bool Bold { get; set; }

    public bool Italics { get; set; }
    
    public bool AnyoneCanMention { get; set; }

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static PlanetRole GetDefault(long planetId)
    {
        return new PlanetRole()
        {
            Name = "Default",
            Id = 0,
            Position = int.MaxValue,
            PlanetId = planetId,
            Color = "#ffffff",
        };
    }

    protected override void OnUpdated(ModelUpdatedEvent<PlanetRole> eventData)
    {
    }

    public override PlanetRole AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        
        return Planet.Roles.Put(this, flags);
    }

    public override PlanetRole RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Roles.Remove(this, skipEvents);
    }

    protected override void OnDeleted()
    {
    }

    // TODO: Model store
    /// <summary>
    /// Requests and caches nodes from the server
    /// </summary>
    public Task LoadPermissionNodesAsync() =>
        Client.PermissionService.FetchPermissionsNodesByRoleAsync(Id, Planet);

    /// <summary>
    /// Returns the permission node for the given channel id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionNode(long targetId)
    {
        if (PermissionsNodes is null)
            await LoadPermissionNodesAsync();

        return PermissionsNodes!.FirstOrDefault(x => x.TargetId == targetId);
    }
}
