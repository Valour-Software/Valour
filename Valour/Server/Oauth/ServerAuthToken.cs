using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Users;
using Valour.Shared.Oauth;

namespace Valour.Server.Oauth
{
    public class ServerAuthToken : AuthToken
    {
        [ForeignKey("User_Id")]
        public virtual ServerUser User { get; set; }
    }
}
