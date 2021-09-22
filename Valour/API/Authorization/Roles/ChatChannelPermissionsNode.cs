using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Planets;
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
    public static async Task<TaskResult<ChatChannelPermissionsNode>> FindAsync(Channel channel, Role role) =>
        await FindAsync(channel.Id, role.Id);


    /// <summary>
    /// Returns the chat channel permissions node for the given id
    /// </summary>
    public static async Task<TaskResult<ChatChannelPermissionsNode>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<ChatChannelPermissionsNode>(id);
            if (cached is not null)
                return new TaskResult<ChatChannelPermissionsNode>(true, "Success: Cached", cached);
        }

        var getResponse = await ValourClient.GetJsonAsync<ChatChannelPermissionsNode>($"api/node/channel/{id}");

        if (getResponse.Success)
        {
            ValourCache.Put(id, getResponse.Data);
            ValourCache.Put((getResponse.Data.Channel_Id, getResponse.Data.Role_Id), getResponse.Data);
        }

        return getResponse;
    }

    /// <summary>
    /// Returns the chat channel permissions node for the given ids
    /// </summary>
    public static async Task<TaskResult<ChatChannelPermissionsNode>> FindAsync(ulong channel_id, ulong role_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<ChatChannelPermissionsNode>((channel_id, role_id));
            if (cached is not null)
                return new TaskResult<ChatChannelPermissionsNode>(true, "Success: Cached", cached);
        }

        var getResponse = await ValourClient.GetJsonAsync<ChatChannelPermissionsNode>($"api/node/channel/{channel_id}/{role_id}");

        if (getResponse.Success)
        {
            ValourCache.Put(getResponse.Data.Id, getResponse.Data);
            ValourCache.Put((channel_id, role_id), getResponse.Data);
        }

        return getResponse;
    }
}

