using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : Item, IPlanetItem, ISharedPlanetRole
{
    #region IPlanetItem implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetItem.GetPlanetAsync(this, refresh);

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
        Red = 255,
        Green = 0,
        Blue = 255
    };

    public static PlanetRole DefaultRole = new PlanetRole()
    {
        Name = "Default",
        Id = long.MaxValue,
        Position = int.MaxValue,
        PlanetId = 0,
        Red = 255,
        Green = 255,
        Blue = 255,
        Permissions = PlanetPermissions.Default,
        ChatPermissions = ChatChannelPermissions.Default,
        CategoryPermissions = Valour.Shared.Authorization.CategoryPermissions.Default,
        VoicePermissions = VoiceChannelPermissions.Default
    };

    // Cached values
    private List<PermissionsNode> PermissionsNodes { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public int Position { get; set; }

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

    // RGB Components for role color
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }

    // Formatting options
    public bool Bold { get; set; }

    public bool Italics { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);

    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

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
            Red = 255,
            Green = 255,
            Blue = 255
        };
    }

    public static async Task<PlanetRole> FindAsync(long id, long planetId, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetRole>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetRole>($"api/roles/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
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
            await ValourCache.Put(node.Id, node, true);
        }

        // Create container if needed
        if (PermissionsNodes == null)
            PermissionsNodes = new List<PermissionsNode>();
        else
            PermissionsNodes.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var node in nodes)
        {
            var cNode = ValourCache.Get<PermissionsNode>(node.Id);

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

        return PermissionsNodes.FirstOrDefault(x => x.TargetId == targetId);
    }
}
