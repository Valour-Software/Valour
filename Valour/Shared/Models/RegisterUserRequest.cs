namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class RegisterUserRequest
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Referrer { get; set; }
    public DateTime DateOfBirth { get; set; }
    public Locality Locality { get; set; }
    public string InviteCode { get; set; }
    public string Source { get; set; }
}
