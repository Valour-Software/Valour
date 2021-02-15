using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Users.Identity
{
    /// <summary>
    /// Assists in password recovery systems
    /// </summary>
    public class PasswordRecovery
    {
        [Key]
        public string Code { get; set; }
        public ulong User_Id { get; set; }
    }
}
