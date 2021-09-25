using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Planets;
using Valour.Api.Roles;
using Valour.Shared;

namespace Valour.Api.Authorization.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class ChatChannelPermissionsNode : Shared.Roles.ChatChannelPermissionsNode
{

    /// <summary>
    /// Returns the chat channel permissions node for the given channel and role
    /// </summary>
    public static async Task<ChatChannelPermissionsNode> FindAsync(Channel channel, Role role) =>
        await FindAsync(channel.Id, role.Id);


    /// <summary>
    /// Returns the chat channel permissions node for the given id
    /// </summary>
    public static async Task<ChatChannelPermissionsNode> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<ChatChannelPermissionsNode>(id);
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<ChatChannelPermissionsNode>($"api/node/channel/{id}");

        if (node is not null)
        {
            ValourCache.Put(id, node);
            ValourCache.Put((node.Channel_Id, node.Role_Id), node);
        }

        return node;
    }

    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async Task<ChatChannelPermissionsNode> FindAsync(ulong channel_id, ulong role_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<ChatChannelPermissionsNode>((channel_id, role_id));
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<ChatChannelPermissionsNode>($"api/node/channel/{channel_id}/{role_id}");

        if (node is not null)
        {
            ValourCache.Put(node.Id, node);
            ValourCache.Put((channel_id, role_id), node);
        }

        return node;
    }
}

