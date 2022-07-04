using System.ComponentModel.DataAnnotations;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Users;

/// <summary>
/// This class is being ripped from the User implementation so we
/// don't have to remove the private info each time we use an API,
/// greatly reducing the mental burden of ensuring security
///  - Spike
/// </summary>
[Table("useremails")]
public class UserEmail
{
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// The user's email address
    /// </summary>
    [Key]
    public string Email { get; set; }

    /// <summary>
    /// True if the email is verified
    /// </summary>
    public bool Verified { get; set; }

    /// <summary>
    /// The user this email belongs to
    /// </summary>
    public ulong UserId { get; set; }
}

