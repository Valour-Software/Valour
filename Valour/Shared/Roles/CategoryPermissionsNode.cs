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
    /// This is a set of permissions for a specific category
    /// </summary>
    public class CategoryPermissionsNode : PermissionsNode
    {
        /// <summary>
        /// The category this node applies to
        /// </summary>
        public ulong Category_Id { get; set; }

        /// <summary>
        /// The permission code for the chat channels within the category
        /// </summary>
        public ulong ChatChannel_Code { get; set; }

        /// <summary>
        /// The permission mask for the chat channels within the category
        /// </summary>
        public ulong ChatChannel_Code_Mask { get; set; }

        /// <summary>
        /// Returns the node code for the chat channel
        /// </summary>
        public PermissionNodeCode GetChatChannelNodeCode()
        {
            return new PermissionNodeCode(ChatChannel_Code, ChatChannel_Code_Mask);
        }

        /// <summary>
        /// Returns the permission state for a given permission
        /// </summary>
        public PermissionState GetChatChannelPermissionState(Permission perm)
        {
            return GetChatChannelNodeCode().GetState(perm);
        }

        /// <summary>
        /// Sets a permission to the given state
        /// </summary>
        public void SetChatChannelPermission(Permission perm, PermissionState state)
        {
            if (state == PermissionState.Undefined)
            {
                // Remove bit from code
                ChatChannel_Code &= ~perm.Value;

                // Remove mask bit
                ChatChannel_Code_Mask &= ~perm.Value;
            }
            else if (state == PermissionState.True)
            {
                // Add mask bit
                ChatChannel_Code_Mask |= perm.Value;

                // Add true bit
                ChatChannel_Code |= perm.Value;
            }
            else
            {
                // Remove mask bit
                ChatChannel_Code_Mask |= perm.Value;

                // Remove true bit
                ChatChannel_Code &= ~perm.Value;
            }
        }
    }
}
