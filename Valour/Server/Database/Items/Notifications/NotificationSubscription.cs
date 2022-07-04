using System.ComponentModel.DataAnnotations;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Notifications;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Notifications;

[Table("notificationsubscriptions")]
public class NotificationSubscription : ISharedNotificationSubscription
{
    [ForeignKey("UserId")]
    public User User { get; set; }

    [Key]
    public ulong Id { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public ulong UserId { get; set; }
    public string Endpoint { get; set; }
    public string Not_Key { get; set; }
    public string Auth { get; set; }
}
