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

namespace Valour.Shared.Messaging
{
    public class MessagePostResponse : TaskResult
    {
        /// <summary>
        /// The final index of the message that was posted
        /// </summary>
        public ulong Index { get; set; }

        public MessagePostResponse(bool success, string response, ulong index) : base(success, response)
        {
            this.Index = index;
        }
    }
}
