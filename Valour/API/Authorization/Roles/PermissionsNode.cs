using Valour.Api.Client;
using Valour.Api.Planets;

namespace Valour.Api.Authorization.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : Shared.Roles.PermissionsNode<PermissionsNode>
{

    /// <summary>
    /// Returns the chat channel permissions node for the given channel and role
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(Channel channel, Role role) =>
        await FindAsync(channel.Id, role.Id);


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
            await ValourCache.Put((node.Target_Id, node.Role_Id), node);
        }

        return node;
    }

    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async Task<PermissionsNode> FindAsync(ulong channel_id, ulong role_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PermissionsNode>((channel_id, role_id));
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<PermissionsNode>($"api/node/channel/{channel_id}/{role_id}");

        if (node is not null)
        {
            await ValourCache.Put(node.Id, node);
            await ValourCache.Put((channel_id, role_id), node);
        }

        return node;
    }
}

