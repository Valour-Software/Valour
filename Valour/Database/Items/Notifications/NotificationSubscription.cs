using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Users;
using Valour.Shared.Items.Notifications;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Notifications;

public class NotificationSubscription : ISharedNotificationSubscription
{
    [ForeignKey("User_Id")]
    public User User { get; set; }

    /// <summary>
    /// The Id of this subscription
    /// </summary>
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Endpoint")]
    public string Endpoint { get; set; }

    [JsonPropertyName("Not_Key")]
    public string Not_Key { get; set; }

    [JsonPropertyName("Auth")]
    public string Auth { get; set; }
}
