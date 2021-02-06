using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Users;

namespace Valour.Server.Email
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class is being ripped from the User implementation so we
    /// don't have to remove the private info each time we use an API,
    /// greatly reducing the mental burden of ensuring security
    ///  - Spike
    /// </summary>
    public class UserEmail
    {
        /// <summary>
        /// The user's email address
        /// </summary>
        [Key]
        public string Email { get; set; }

        /// <summary>
        /// True if the email is verified
        /// </summary>
        public bool Verified { get; set; }

        /// <summary>
        /// The user this email belongs to
        /// </summary>
        public ulong User_Id { get; set; }

        /// <summary>
        /// Returns the user for this email entry
        /// </summary>
        public async Task<User> GetUserAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.Users.FindAsync(User_Id);
            }
        }
    }
}
