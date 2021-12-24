using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Users;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Authorization;

public class OauthApp : NamedItem, ISharedOauthApp {

    [ForeignKey("Owner_Id")]
    [JsonIgnore]
    public virtual User Owner { get; set; }

    /// <summary>
    /// The secret key for the app
    /// </summary>
    [JsonPropertyName("Secret")]
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    [JsonPropertyName("Owner_Id")]
    public ulong Owner_Id { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    [JsonPropertyName("Uses")]
    public int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    [JsonPropertyName("Image_Url")]
    public string Image_Url { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.OauthApp;
}