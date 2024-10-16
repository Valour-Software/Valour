using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : ClientModel<long>, IClientPlanetModel, ISharedPlanetRole
{
    #region IPlanetModel implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IClientPlanetModel.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/roles";

    #endregion

    // Coolest role on this damn platform.
    // Fight me.
    public static PlanetRole VictorRole = new PlanetRole()
    {
        Name = "Victor Class",
        Id = long.MaxValue,
        Position = int.MaxValue,
        PlanetId = 0,
        Color = "ff00ff",
        AnyoneCanMention = false,
    };

    public static PlanetRole DefaultRole = new PlanetRole()
    {
        Name = "Default",
        Id = long.MaxValue,
        Position = int.MaxValue,
        PlanetId = 0,
        Color = "#ffffff",
        Permissions = PlanetPermissions.Default,
        ChatPermissions = ChatChannelPermissions.Default,
        CategoryPermissions = Valour.Shared.Authorization.CategoryPermissions.Default,
        VoicePermissions = VoiceChannelPermissions.Default,
        AnyoneCanMention = false,
    };

    // Cached values
    private List<PermissionsNode> PermissionsNodes { get; set; }

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
            Id = long.MaxValue,
            Position = int.MaxValue,
            PlanetId = planetId,
            Color = "#ffffff",
        };
    }

    public static async Task<PlanetRole> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ModelCache<,>.Get<PlanetRole>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetRole>($"api/roles/{id}")).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }

    protected override async Task OnUpdated(ModelUpdateEvent eventData)
    {
        var planet = await GetPlanetAsync();
        await planet.NotifyRoleUpdateAsync(this, eventData);
    }

    protected override async Task OnDeleted()
    {
        var planet = await GetPlanetAsync();
        await planet.NotifyRoleDeleteAsync(this);
    }

    /// <summary>
    /// Requests and caches nodes from the server
    /// </summary>
    public async Task LoadPermissionNodesAsync()
    {
        var nodes = (await Node.GetJsonAsync<List<PermissionsNode>>($"{IdRoute}/nodes")).Data;
        if (nodes is null)
            return;

        // Update cache values
        foreach (var node in nodes)
        {
            // Skip event for bulk loading
            await ModelCache<,>.Put(node.Id, node, true);
        }

        // Create container if needed
        if (PermissionsNodes == null)
            PermissionsNodes = new List<PermissionsNode>();
        else
            PermissionsNodes.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var node in nodes)
        {
            var cNode = ModelCache<,>.Get<PermissionsNode>(node.Id);

            if (cNode is not null)
                PermissionsNodes.Add(cNode);
        }
    }

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
