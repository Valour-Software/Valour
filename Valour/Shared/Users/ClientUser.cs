using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    public class ClientUser
    {
        /// <summary>
        /// The Id of the user
        /// </summary>
        [Key]
        public ulong Id { get; set; }

        /// <summary>
        /// The main display name for the user
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// The url for the user's profile picture
        /// </summary>
        public string Pfp_Url { get; set; }
    }
}
