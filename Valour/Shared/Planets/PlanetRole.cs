using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Planets
{
    public class PlanetRole
    {
        /// <summary>
        /// The name of the role
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The authority of this role: Higher authority is more powerful
        /// </summary>
        int Authority { get; set; }

        /// <summary>
        /// The ID of the planet this role belongs to
        /// </summary>
        public byte[] Planet_Id { get; set; }
    }
}
