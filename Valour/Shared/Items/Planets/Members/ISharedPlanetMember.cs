using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public interface ISharedPlanetMember : ISharedPlanetItem
{
    /// <summary>
    /// The user within the planet
    /// </summary>
    ulong UserId { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    string MemberPfp { get; set; }
}

