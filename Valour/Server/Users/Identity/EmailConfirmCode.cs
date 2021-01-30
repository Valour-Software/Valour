using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users.Identity
{
    /// <summary>
    /// Allows tracking of email verification codes
    /// </summary>
    public class EmailConfirmCode
    {
        /// <summary>
        /// The code for the email verification
        /// </summary>
        [Key]
        public string Code { get; set; }

        /// <summary>
        /// The user this code is verifying
        /// </summary>
        public ulong User_Id { get; set; }
    }
}
