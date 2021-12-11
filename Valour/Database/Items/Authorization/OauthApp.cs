using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Authorization;

public class OauthApp : Valour.Shared.Items.Authorization.OauthApp {
    [ForeignKey("Owner_Id")]
    public virtual User Owner { get; set; }
}