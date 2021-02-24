using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// This is a set of permissions for a specific channel
    /// </summary>
    public class ChannelPermissionsNode
    {
        /// <summary>
        /// The ID of this permission node
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The permission code that this node has set
        /// </summary>
        public ulong Code { get; set; }

        /// <summary>
        /// A mask used to determine if code bits are disabled
        /// </summary>
        public ulong Code_Mask { get; set; }

        /// <summary>
        /// The channel this node applies to
        /// </summary>
        public ulong Channel_Id { get; set; }

        /// <summary>
        /// The planet this node applies to
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The role this permissions node belongs to
        /// </summary>
        public ulong Role_Id { get; set; }


        /// <summary>
        /// Returns the node code for this permission node
        /// </summary>
        public PermissionNodeCode GetNodeCode()
        {
            return new PermissionNodeCode(Code, Code_Mask);
        }

        /// <summary>
        /// Returns the permission state for a given permission
        /// </summary>
        public PermissionState GetPermissionState(Permission perm)
        {
            return GetNodeCode().GetState(perm);
        }
    }
}
