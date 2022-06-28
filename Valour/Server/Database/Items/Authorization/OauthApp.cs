using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Authorization;

public class OauthApp : Shared.Items.Authorization.OauthApp
{
    [ForeignKey("OwnerId")]
    [JsonIgnore]
    public virtual User Owner { get; set; }
}