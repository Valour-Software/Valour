using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This role class is used for the backend, this is
    /// not the same thing as a Planet Role
    /// </summary>
    public class Role
    {
        /// <summary>
        /// The ID of this role
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The code for this role
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// The name of this role
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The weight of the role
        /// </summary>
        public int Weight { get; set; }
    }
}
