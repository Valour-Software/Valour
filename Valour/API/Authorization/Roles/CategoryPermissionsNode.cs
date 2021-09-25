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

public class CategoryPermissionsNode : Shared.Roles.CategoryPermissionsNode
{
    /// <summary>
    /// Returns the category permissions node for the given channel and role
    /// </summary>
    public static async Task<CategoryPermissionsNode> FindAsync(Category channel, Role role) =>
        await FindAsync(channel.Id, role.Id);


    /// <summary>
    /// Returns the category permissions node for the given id
    /// </summary>
    public static async Task<CategoryPermissionsNode> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<CategoryPermissionsNode>(id);
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<CategoryPermissionsNode>($"api/node/category/{id}");

        if (node is not null)
        {
            ValourCache.Put(id, node);
            ValourCache.Put((node.Category_Id, node.Role_Id), node);
        }

        return node;
    }

    /// <summary>
    /// Returns the category permissions node for the given ids
    /// </summary>
    public static async Task<CategoryPermissionsNode> FindAsync(ulong category_id, ulong role_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<CategoryPermissionsNode>((category_id, role_id));
            if (cached is not null)
                return cached;
        }

        var node = await ValourClient.GetJsonAsync<CategoryPermissionsNode>($"api/node/category/{category_id}/{role_id}");

        if (node is not null)
        {
            ValourCache.Put(node.Id, node);
            ValourCache.Put((category_id, role_id), node);
        }

        return node;
    }
}

