using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetInvite : SyncedItem<PlanetInvite>, ISharedPlanetInvite
{
    /// <summary>
    /// the invite code
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// The planet the invite is for
    /// </summary>
    public ulong PlanetId { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    public ulong IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime Issued { get; set; }

    /// <summary>
    /// The time when this invite expires. Null for never.
    /// </summary>
    public DateTime? Expires { get; set; }

    public bool IsPermanent() =>
        ISharedPlanetInvite.IsPermanent(this);

    /// <summary>
    /// Returns the invite for the given invite code
    /// </summary>
    public static async Task<PlanetInvite> FindAsync(string code, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetInvite>(code);
            if (cached is not null)
                return cached; 
        }

        var invResult = await ValourClient.GetJsonAsync<PlanetInvite>($"api/invite/{code}");

        if (invResult is not null)
            await ValourCache.Put(code, invResult);

        return invResult;
    }

    /// <summary>
    /// Returns the name of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetNameAsync() =>
        await ValourClient.GetAsync($"api/invite/{Code}/planet/name") ?? "<Not found>";
    
    /// <summary>
    /// Returns the icon of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetIconUrl() =>
        await ValourClient.GetAsync($"api/invite/{Code}/planet/icon_url") ?? "";
}

