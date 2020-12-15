using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users.Identity
{
    public class RoleClaimPermission
    {
        /// <summary>
        /// The role ID
        /// </summary>
        public int Role_Id { get; set; }

        /// <summary>
        /// The permission ID
        /// </summary>
        public int ClaimPermission_Id { get; set; }

        // Allows foreign key magic in the future
        public virtual Role Role { get; set; }
        public virtual ClaimPermission ClaimPermission { get; set; }
    }
}
