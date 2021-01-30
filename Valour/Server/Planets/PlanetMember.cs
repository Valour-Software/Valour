using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet and is used to represent membership
    /// </summary>
    public class PlanetMember
    {
        /// <summary>
        /// The Id of this member object
        /// </summary>
        [Key]
        public ulong Id { get; set; }

        /// <summary>
        /// The user within the planet
        /// </summary>
        public ulong User_Id { get; set; }

        /// <summary>
        /// The planet the user is within
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// Returns the user (async)
        /// </summary>
        public async Task<User> GetUserAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.Users.FindAsync(User_Id);
            }
        }

        /// <summary>
        /// Returns the user
        /// </summary>
        public User GetUser()
        {
            return GetUserAsync().Result;
        }

        /// <summary>
        /// Returns the planet (async)
        /// </summary>
        public async Task<Planet> GetPlanetAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.Planets.FindAsync(Planet_Id);
            }
        }

        /// <summary>
        /// Returns the planet
        /// </summary>
        public Planet GetPlanet()
        {
            return GetPlanetAsync().Result;
        }

        public async Task<bool> IsBanned(ValourDB Context, ulong planetid)
        {
            PlanetBan ban = await Context.PlanetBans.Where(x => x.Planet_Id == planetid && x.User_Id == this.User_Id).FirstOrDefaultAsync();

            if (ban == null) {
                return false;
            }

            if (ban == null) {
                return false;
            }

            else {
                return true;
            }

        }
    }
}
