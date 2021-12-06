using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Notifications;

public class NotificationSubscription : Shared.Notifications.NotificationSubscription
{
    [ForeignKey("User_Id")]
    public ServerUser User { get; set; }
}
