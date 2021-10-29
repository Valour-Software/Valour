using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Email;
using Valour.Server.Planets;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    public class ServerUser : User<ServerUser>
    {
        [InverseProperty("User")]
        public virtual UserEmail Email { get; set; }
        
        [InverseProperty("User")]
        public virtual ICollection<ServerPlanetMember> Membership { get; set; }
    }
}
