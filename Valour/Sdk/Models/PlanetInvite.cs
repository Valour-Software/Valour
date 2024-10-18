using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetInvite : ClientPlanetModel<PlanetInvite, string>, ISharedPlanetInvite
{
    public override string BaseRoute => ISharedPlanetInvite.BaseRoute;    
    public override string IdRoute => ISharedPlanetInvite.GetIdRoute(Id);

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
    
    public long PlanetId { get; set; }
    public override long? GetPlanetId() => PlanetId;

    public bool IsPermanent() =>
        ISharedPlanetInvite.IsPermanent(this);

    /// <summary>
    /// Returns the invite for the given invite code (id)
    /// </summary>
    public static async Task<PlanetInvite> FindAsync(string code, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = Cache.Get(code);
            if (cached is not null)
                return cached;
        }
        
        var invResult = (await ValourClient.PrimaryNode.GetJsonAsync<PlanetInvite>(ISharedPlanetInvite.GetIdRoute(code))).Data;

        if (invResult is not null)
            return await invResult.SyncAsync();

        return null;
    }

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
        (await ValourClient.PrimaryNode.GetJsonAsync<InviteScreenModel>(
            $"{ISharedPlanetInvite.BaseRoute}/{code}/screen")).Data;
}

