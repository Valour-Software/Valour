using Valour.Api.Client;
using Valour.Shared;

namespace Valour.Api.Authorization.Roles;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class Role : Shared.Roles.PlanetRole
{
    /// <summary>
    /// Returns the planet role for the given id
    /// </summary>
    public static async Task<Role> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Role>(id);
            if (cached is not null)
                return cached;
        }

        var role = await ValourClient.GetJsonAsync<Role>($"api/role/{id}");

        if (role is not null)
            ValourCache.Put(id, role);

        return role;
    }
}
