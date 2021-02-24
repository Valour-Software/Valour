using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Planets;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Roles
{
    public class ServerPlanetRoleMember : PlanetRoleMember
    {
        [ForeignKey("Member_Id")]
        public virtual ServerPlanetMember Member { get; set; }

        [ForeignKey("Role_Id")]
        public virtual ServerPlanetRole Role { get; set; }
    }
}
