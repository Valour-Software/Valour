using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Channels
{
    /// <summary>
    /// Represents a single chat channel within a planet
    /// </summary>
    public class PlanetChatChannel : IChatChannel
    {
        /// <summary>
        /// The Id of this channel
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this channel
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Id of the planet this channel belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The amount of messages ever sent in the channel
        /// </summary>
        public ulong Message_Count { get; set; }

        /// <summary>
        /// The id of the parent category, is null if theres no parent
        /// </summary>
        public ulong? Parent_Id { get; set;}

        /// <summary>
        /// Is the position in the category/channel list
        /// </summary>
        public ushort Position { get; set; }
    }
}
