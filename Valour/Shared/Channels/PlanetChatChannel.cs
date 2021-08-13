using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Channels
{
    /// <summary>
    /// Represents a single chat channel within a planet
    /// </summary>
    public class PlanetChatChannel : Valour.Shared.Planets.ChannelListItem, IChatChannel
    {
        /// <summary>
        /// The amount of messages ever sent in the channel
        /// </summary>
        public ulong Message_Count { get; set; }

        /// <summary>
        /// If true, this channel will inherit the permission nodes
        /// from the category it belongs to
        /// </summary>
        public bool Inherits_Perms { get; set; }
    }
}
