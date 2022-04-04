using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class PlanetBan : PlanetBanBase, INodeSpecific
{

}
