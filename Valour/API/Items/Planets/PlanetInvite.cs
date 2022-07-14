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

public class PlanetInvite : PlanetItem, ISharedPlanetInvite
{
    /// <summary>
    /// the invite code
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    public long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time when this invite expires. Null for never.
    /// </summary>
    public DateTime? TimeExpires { get; set; }

    public bool IsPermanent() =>
        ISharedPlanetInvite.IsPermanent(this);

    /// <summary>
    /// Returns the invite for the given invite code
    /// </summary>
    public static async Task<PlanetInvite> FindAsync(string code, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetInvite>(code);
            if (cached is not null)
                return cached; 
        }

        var invResult = await ValourClient.GetJsonAsync<PlanetInvite>($"api/{nameof(PlanetInvite)}/{code}");

        if (invResult is not null)
            await invResult.AddToCache();

        return invResult;
    }

    public override async Task AddToCache()
    {
        await ValourCache.Put(Code, this);
    }

    public override string IdRoute => $"{BaseRoute}/{Code}";
    public override string BaseRoute => $"/api/{nameof(PlanetInvite)}";

    /// <summary>
    /// Returns the name of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetNameAsync() =>
        await ValourClient.GetJsonAsync<string>($"{IdRoute}/planetname") ?? "<Not found>";
    
    /// <summary>
    /// Returns the icon of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetIconUrl() =>
        await ValourClient.GetJsonAsync<string>($"{IdRoute}/planeticon") ?? "";
}

