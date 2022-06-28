using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public interface ISharedPlanetInvite
{
    /// <summary>
    /// the invite code
    /// </summary>
    string Code { get; set; }

    /// <summary>
    /// The planet the invite is for
    /// </summary>
    ulong PlanetId { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    ulong IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    DateTime Issued { get; set; }

    /// <summary>
    /// The time when this invite expires. Null for never.
    /// </summary>
    DateTime? Expires { get; set; }

    public bool IsPermanent()
    {
        return (Expires == null);
    }
}

