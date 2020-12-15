using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users.Identity
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// Represent a permission for a claim (this is not for Planet permissions 
    /// but is for low level user management)
    /// </summary>
    public class ClaimPermission
    {
        /// <summary>
        /// The ID of this permission
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The code for this permission
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// The name for this permission
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The weight for this permission
        /// </summary>
        public int Weight { get; set; }
    }
}
