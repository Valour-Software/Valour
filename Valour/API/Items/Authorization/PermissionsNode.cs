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

public class PermissionsNode : ISharedPermissionsNode
{
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
            await ValourCache.Put((node.TargetId, (node.RoleId, node.ItemType)), node);
        }

        return node;
    }

    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(ulong targetId, ulong roleId, ItemType type, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PermissionsNode>((targetId, (roleId, type)));
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<PermissionsNode>($"api/node/{targetId}/{roleId}");

        if (node is not null)
        {
            await ValourCache.Put(node.Id, node);
            await ValourCache.Put((targetId, (roleId, type)), node);
        }

        return node;
    }
}

