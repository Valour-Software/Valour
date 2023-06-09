using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Valour.Api.Client;
using Valour.Api.Items;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : Item, ISharedPermissionsNode
{
    /// <summary>
    /// The planet this node belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    public long Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    public long Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    public long TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    public PermChannelType TargetType { get; set; }

    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() =>
        ISharedPermissionsNode.GetNodeCode(this);

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm, bool ignoreviewperm = false) =>
        ISharedPermissionsNode.GetPermissionState(this, perm, ignoreviewperm);

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) =>
        ISharedPermissionsNode.SetPermission(this, perm, state);

    /// <summary>
    /// Returns the chat channel permissions node for the given channel and role
    /// </summary>
    public static ValueTask<PermissionsNode> FindAsync(PlanetChatChannel channel, PlanetRole role, PermChannelType targetType) =>
        FindAsync(channel.Id, role.Id, targetType);

    public override string IdRoute => $"{BaseRoute}/{TargetType}/{TargetId}/{RoleId}";

    public override string BaseRoute => $"api/permissionsnodes";


    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async ValueTask<PermissionsNode> FindAsync(long targetId, long roleId, PermChannelType type, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PermissionsNode>((targetId, (roleId, type)));
            if (cached is not null)
                return cached;
        }

        var permNode = (await ValourClient.PrimaryNode.GetJsonAsync<PermissionsNode>($"api/permissionsnodes/{type}/{targetId}/{roleId}", true)).Data;

        if (permNode is not null)
            await permNode.AddToCache();

        return permNode;
    }

    public override async Task AddToCache()
    {
        await ValourCache.Put(Id, this);
        await ValourCache.Put((TargetId, (RoleId, TargetType)), this);
    }

    public static async Task<List<PermissionsNode>> GetAllForPlanetAsync(long planetId)
    {
        var nodes = (await ValourClient.PrimaryNode.GetJsonAsync<List<PermissionsNode>>($"api/permissionsnodes/all/{planetId}")).Data;

        var results = new List<PermissionsNode>();
        
        foreach (var node in nodes)
        {
            // Add or update in cache
            await node.AddToCache();
            
            // Put cached node in results
            results.Add(ValourCache.Get<PermissionsNode>(node.Id));
        }
        
        // Return results
        return results;
    }
}

