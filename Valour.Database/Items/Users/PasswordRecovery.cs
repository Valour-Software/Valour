using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Users;

/// <summary>
/// Assists in password recovery systems
/// </summary>
public class PasswordRecovery
{
    [Key]
    public string Code { get; set; }
    public ulong User_Id { get; set; }

    [ForeignKey("User_Id")]
    public virtual ServerUser User { get; set; }
}

