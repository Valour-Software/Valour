using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Roles
{
    public class PlanetRoleMember
    {
        public ulong Id { get; set; }
        public ulong User_Id { get; set; }
        public ulong Role_Id { get; set; }
        public ulong Planet_Id { get; set; }
    }
}
