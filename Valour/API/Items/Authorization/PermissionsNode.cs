using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets.Channels;
using Valour.Api.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Api.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : Item<PermissionsNode>, ISharedPermissionsNode
{
    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    [JsonPropertyName("Code")]
    public ulong Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    [JsonPropertyName("Mask")]
    public ulong Mask { get; set; }

    /// <summary>
    /// The planet this node applies to
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    [JsonPropertyName("Role_Id")]
    public ulong Role_Id { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    [JsonPropertyName("Target_Id")]
    public ulong Target_Id { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    [JsonPropertyName("Target_Type")]
    public ItemType Target_Type { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PermissionsNode;


    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() => 
        ((ISharedPermissionsNode)this).GetNodeCode();

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm) => 
        ((ISharedPermissionsNode)this).GetPermissionState(perm);

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) => 
        ((ISharedPermissionsNode)this).SetPermission(perm, state);



    /// <summary>
    /// Returns the chat channel permissions node for the given channel and role
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(PlanetChatChannel channel, PlanetRole role, ItemType itemType) =>
        await FindAsync(channel.Id, role.Id, itemType);


    /// <summary>
    /// Returns the chat channel permissions node for the given id
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PermissionsNode>(id);
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<PermissionsNode>($"api/node/channel/{id}");

        if (node is not null)
        {
            await ValourCache.Put(id, node);
            await ValourCache.Put((node.Target_Id, (node.Role_Id, node.ItemType)), node);
        }

        return node;
    }

    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(ulong target_id, ulong role_id, ItemType type, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PermissionsNode>((target_id, (role_id, type)));
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<PermissionsNode>($"api/node/{target_id}/{role_id}");

        if (node is not null)
        {
            await ValourCache.Put(node.Id, node);
            await ValourCache.Put((target_id, (role_id, type)), node);
        }

        return node;
    }
}

