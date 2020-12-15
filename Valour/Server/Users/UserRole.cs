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
    /// Used to link a user to a specific role
    /// </summary>
    public class UserRole
    {
        /// <summary>
        /// The ID of the user
        /// </summary>
        public ulong User_Id { get; set; }

        /// <summary>
        /// The ID of the role
        /// </summary>
        public int Role_Id { get; set; }

        // Allows for easy inclusion in DB queries (foreign key)
        public virtual User User { get; set; }
        public virtual Role Role { get; set; }
    }
}
