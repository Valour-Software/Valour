using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Shared.Items;
using Valour.Shared.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items
{
    /// <summary>
    /// Represents a single chat channel within a planet
    /// </summary>
    public interface IPlanetChatChannel : IChannelListItem
    {
        /// <summary>
        /// The amount of messages ever sent in the channel
        /// </summary>
        [JsonPropertyName("Message_Count")]
        public ulong Message_Count { get; set; }

        /// <summary>
        /// If true, this channel will inherit the permission nodes
        /// from the category it belongs to
        /// </summary>
        [JsonPropertyName("Inherits_Perms")]
        public bool Inherits_Perms { get; set; }
    }
}
