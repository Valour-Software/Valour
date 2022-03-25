using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Planets.Members;

public interface ISharedPlanetRoleMember
{
    ulong User_Id { get; set; }
    ulong Role_Id { get; set; }
    ulong Planet_Id { get; set; }
    ulong Member_Id { get; set; }
}

