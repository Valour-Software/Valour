using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Notifications;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Notifications;

public class NotificationSubscription : Shared.Items.Notifications.NotificationSubscription
{
    [ForeignKey("UserId")]
    public User User { get; set; }
}
