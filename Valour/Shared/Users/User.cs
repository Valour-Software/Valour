using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Users
{
    /// <summary>
    /// This is the base User object, which contains everything needed for public use
    /// </summary>
    public class User
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

        /// <summary>
        /// The Date and Time that the user joined Valour
        /// </summary>
        public DateTime Join_DateTime { get; set; }

        /// <summary>
        /// True if the user is a bot
        /// </summary>
        public bool Bot { get; set; }

        /// <summary>
        /// True if the account has been disabled
        /// </summary>
        public bool Disabled { get; set; }
    }
}
