using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;

namespace Valour.Server.Users.Identity
{
    /// <summary>
    /// Allows tracking of email verification codes
    /// </summary>
    public class EmailConfirmCode
    {
        [ForeignKey("User_Id")]
        public virtual ServerUser User { get; set; }

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
