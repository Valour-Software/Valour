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

namespace Valour.Shared.Roles
{
    /// <summary>
    /// A permission node is a set of permissions for a specific thing
    /// This is a set of permissions for a specific channel
    /// </summary>
    public class ChannelPermissionsNode
    {
        /// <summary>
        /// The permission code that this node has set
        /// </summary>
        public ulong Code { get; set; }

        /// <summary>
        /// The channel this node applies to
        /// </summary>
        public ulong Channel_Id { get; set; }
    }
}
