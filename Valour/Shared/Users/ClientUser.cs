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
        // 16 bytes
        /// <summary>
        /// Store user id on database in byte form to cut data usage in half
        /// </summary>
        [Key]
        public byte[] userid_bytes { get; set; }

        /// <summary>
        /// Caches the string version of the user id to save future work
        /// </summary>
        private string userid_string = null;

        // 36 chars (at least 36 bytes)
        /// <summary>
        /// The Id of the user
        /// </summary>
        public string UserId
        {
            get
            {
                if (userid_string == null)
                {
                    userid_string = new Guid(userid_bytes).ToString();
                }

                return userid_string;
            }
        }

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
