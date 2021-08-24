using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Valour.Shared.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Roles
{
    /// <summary>
    /// A permission node is a set of permissions for a specific thing
    /// This is a set of permissions for a specific chat channel
    /// </summary>
    public class ChatChannelPermissionsNode : PermissionsNode
    {
        /// <summary>
        /// The channel this node applies to
        /// </summary>
        [JsonPropertyName("Channel_Id")]
        public ulong Channel_Id { get; set; }
    }
}
