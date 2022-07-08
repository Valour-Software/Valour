using System.ComponentModel.DataAnnotations;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Notifications;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Notifications;

[Table("notification_subscriptions")]
public class NotificationSubscription : ISharedNotificationSubscription
{
    [ForeignKey("UserId")]
    [JsonIgnore]
    public User User { get; set; }

    [Key]
    [Column("id")]
    public long Id {get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("endpoint")]
    public string Endpoint { get; set; }

    [Column("not_key")]
    public string Not_Key { get; set; }

    [Column("auth")]
    public string Auth { get; set; }
}
