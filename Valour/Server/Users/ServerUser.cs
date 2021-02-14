using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Email;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    public class ServerUser : User
    {
        [InverseProperty("User")]
        public virtual UserEmail Email { get; set; }
    }
}
