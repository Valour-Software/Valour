using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Messages
{
    /*  Valour - A free and secure chat client
    *  Copyright (C) 2021 Vooper Media LLC
    *  This program is subject to the GNU Affero General Public license
    *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
    */

    public enum MentionType
    {
        Member,
        Channel,
        Category,
        Role
    }

    /// <summary>
    /// A member mention is used to refer to a member within a message
    /// </summary>
    public class Mention
    {
        /// <summary>
        /// The type of mention this is
        /// </summary>
        public MentionType Type { get; set; }

        /// <summary>
        /// The item id being mentioned
        /// </summary>
        public ulong Target_Id { get; set; }

        /// <summary>
        /// The position of the mention, in chars.
        /// For example, the message "Hey @SpikeViper!" would have Position = 4
        /// </summary>
        public ushort Position { get; set; }

        /// <summary>
        /// The length of this mention, in chars
        /// </summary>
        public ushort Length { get; set; }
    }
}
