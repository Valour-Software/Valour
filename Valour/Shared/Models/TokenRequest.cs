namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// Signs in with one method: email and password, a ticket from signing in with
/// Google or Discord, or a device key signature. MultiFactorCode applies to the
/// first two.
/// </summary>
public class TokenRequest
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string MultiFactorCode { get; set; }

    /// <summary>
    /// A sign-in ticket from a Google or Discord sign-in, with the verifier the
    /// client generated when it started that sign-in.
    /// </summary>
    public string ExternalTicket { get; set; }
    public string ExternalVerifier { get; set; }

    /// <summary>
    /// A device key sign-in: the key's ID, the challenge the server issued,
    /// and the device's signature over the challenge.
    /// </summary>
    public string DeviceKeyId { get; set; }
    public string DeviceChallengeId { get; set; }
    public string DeviceSignature { get; set; }
}


