using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Messaging;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Messaging
{
    public class ServerMessage
    {
        [Key]
        public byte[] Hash { get; set; }

        /// <summary>
        /// Returns true if the client message matches this server message
        /// </summary>
        public bool EqualsMessage(ClientPlanetMessage message)
        {
            return (Hash == message.GetHash());
        }
    }
}
