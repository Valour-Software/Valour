using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Valour.Shared.Users;

namespace Valour.Shared.Messaging
{
    public class ClientPlanetMessage : ClientMessage
    {
        /// <summary>
        /// The author of this message
        /// </summary>
        public ClientPlanetUser Author { get; set; }
    }
}
