using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet and is used to represent membership
    /// </summary>
    public class BannedMember
    {
        /// <summary>
        /// The Id of this member object
        /// </summary>
        [Key]
        public ulong Id { get; set; }

        /// <summary>
        /// The user that was panned
        /// </summary>
        public ulong User_Id { get; set; }

        /// <summary>
        /// The planet the user was within
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The user that banned the user
        /// </summary>
        public ulong Banner { get; set; }

        /// <summary>
        /// The reason for the ban
        /// </summary>
        public ulong Reason { get; set; }
    }
}
