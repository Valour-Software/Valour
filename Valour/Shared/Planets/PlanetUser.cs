using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet
    /// </summary>
    class PlanetUser
    {
        /// <summary>
        /// The user within the planet
        /// </summary>
        public ulong User_Id { get; set; }

        /// <summary>
        /// The planet the user is within
        /// </summary>
        public ulong Planet_Id { get; set; }
    }
}
