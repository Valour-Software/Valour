using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

public class PlanetRoleMember : Shared.Items.Planets.Members.PlanetRoleMember
{
    [ForeignKey("Member_Id")]
    public virtual PlanetMember Member { get; set; }

    [ForeignKey("Role_Id")]
    public virtual PlanetRole Role { get; set; }
}

