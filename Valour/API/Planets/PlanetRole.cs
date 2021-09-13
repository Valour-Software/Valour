using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.API.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : Shared.Roles.PlanetRole
{
    /// <summary>
    /// Returns the planet role for the given id
    /// </summary>
    public static async Task<TaskResult<PlanetRole>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Roles.ContainsKey(id))
            return new TaskResult<PlanetRole>(true, "Success: Cached", ValourCache.Roles[id]);

        var getResponse = await ValourClient.GetJsonAsync<PlanetRole>($"api/role/{id}");

        if (getResponse.Success)
            ValourCache.Roles[id] = getResponse.Data;

        return getResponse;
    }
}
