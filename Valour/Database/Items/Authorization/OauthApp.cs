using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items;
using Valour.Database.Items.Users;
using Valour.Shared.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Authorization;

[Table("oauth_apps")]
public class OauthApp : Item, ISharedOauthApp
{
    [ForeignKey("OwnerId")]
    [JsonIgnore]
    public virtual User Owner { get; set; }

    /// <summary>
    /// The secret key for the app
    /// </summary>
    [Column("secret")]
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    [Column("uses")]
    public int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    [Column("image_url")]
    public string ImageUrl { get; set; }

    /// <summary>
    /// The name of the app
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The redirect url for authorization
    /// </summary>
    [Column("redirect_url")]
    public string RedirectUrl { get; set; }
}