using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Users;

/// <summary>
/// Allows tracking of email verification codes
/// </summary>
public class EmailConfirmCode
{
    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    /// <summary>
    /// The code for the email verification
    /// </summary>
    [Key]
    public string Code { get; set; }

    /// <summary>
    /// The user this code is verifying
    /// </summary>
    public ulong UserId { get; set; }
}

