
using Valour.Api.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class Invite : Shared.Planets.PlanetInvite
{
    /// <summary>
    /// Returns the invite for the given invite code
    /// </summary>
    public static async Task<TaskResult<Invite>> FindAsync(string code, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Invite>(code);
            if (cached is not null)
                return new TaskResult<Invite>(true, "Success: Cached", cached); 
        }

        var invResult = await ValourClient.GetJsonAsync<Invite>($"api/invite/{code}");

        if (invResult.Success)
            ValourCache.Put(code, invResult.Data);

        return invResult;
    }

    /// <summary>
    /// Returns the name of the invite's planet
    /// </summary>
    public async Task<TaskResult<string>> GetPlanetNameAsync()
    {
        return await ValourClient.GetJsonAsync<string>($"api/invite/{Code}/planet/name");
    }

    /// <summary>
    /// Returns the icon of the invite's planet
    /// </summary>
    public async Task<TaskResult<string>> GetPlanetIconUrl()
    {
        return await ValourClient.GetJsonAsync<string>($"api/invite/{Code}/planet/icon_url");
    }
}

