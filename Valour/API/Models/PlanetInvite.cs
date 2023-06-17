using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Models.Messages;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetInvite : Item, IPlanetModel, ISharedPlanetInvite
{
    #region IPlanetModel implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetModel.GetPlanetAsync(this, refresh);

    public override string BaseRoute => $"api/invites";

    #endregion

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

        var invResult = (await ValourClient.PrimaryNode.GetJsonAsync<PlanetInvite>($"api/invites/{code}")).Data;

        if (invResult is not null)
            await invResult.AddToCache();

        return invResult;
    }

    public override async Task AddToCache<T>(T item)
    {
        await ValourCache.Put(Code, this);
    }

    public override string IdRoute => $"{BaseRoute}/{Code}";

    /// <summary>
    /// Returns the name of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetNameAsync() =>
        (await Node.GetJsonAsync<string>($"{IdRoute}/planetname")).Data ?? "<Not found>";

    /// <summary>
    /// Returns the icon of the invite's planet
    /// </summary>
    public async Task<string> GetPlanetIconUrl() =>
        (await Node.GetJsonAsync<string>($"{IdRoute}/planeticon")).Data ?? "";
}

