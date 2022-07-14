using System.ComponentModel.DataAnnotations;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Users;

/// <summary>
/// Allows tracking of email verification codes
/// </summary>
[Table("email_confirm_codes")]
public class EmailConfirmCode
{
    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    /// <summary>
    /// The code for the email verification
    /// </summary>
    [Key]
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user this code is verifying
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
}

