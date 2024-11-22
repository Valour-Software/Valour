using System.Net.Http.Json;
using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetInvite : ClientModel, IPlanetModel, ISharedPlanetInvite
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
            await invResult.AddToCache(invResult);

        return invResult;
    }

    public override async Task AddToCache<T>(T item, bool skipEvent = false)
    {
        await ValourCache.Put(Code, this, skipEvent);
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

    public static async Task<InviteScreenModel> GetInviteScreenData(string code) =>
        (await (await ValourClient.Http.GetAsync($"{ValourClient.BaseAddress}api/invites/{code}/screen")).Content.ReadFromJsonAsync<InviteScreenModel>());
}

