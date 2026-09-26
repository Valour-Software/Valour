namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
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
    public string InviteCode { get; set; }
    public string Source { get; set; }

    /// <summary>
    /// A one-time registration attestation. This value is not persisted.
    /// </summary>
    public bool IsNotTexasResident { get; set; }

    /// <summary>
    /// Set when signing up with Google or Discord instead of a password. The
    /// email comes from the linked account, so Email and Password are ignored.
    /// </summary>
    public string ExternalTicket { get; set; }
    public string ExternalVerifier { get; set; }

    /// <summary>
    /// Use the linked account's profile picture as the new account's avatar.
    /// </summary>
    public bool UseProviderAvatar { get; set; }
}
