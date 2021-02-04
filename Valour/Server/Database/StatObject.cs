using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Server.Database
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet and is used to represent membership
    /// </summary>
    public class StatObject
    {
        /// <summary>
        /// The Id of this object
        /// </summary>
        [Key]
        public ulong Id { get; set;}

        public int messagesSent { get; set;}

        public int userCount { get; set;}
        public int planetCount { get; set;}
        public int planetmemberCount { get; set;}
        public int channelCount { get; set;}
        public int categoryCount { get; set;}

        public int message24hCount { get; set;}

        public DateTime Time { get; set;}

        public StatObject() {
            
        }


    }
}
