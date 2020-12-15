using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users
{
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
