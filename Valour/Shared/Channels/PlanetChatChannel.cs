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
        public uint Id { get; set; }

        /// <summary>
        /// The name of this channel
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Id of the planet this channel belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }
    }
}
