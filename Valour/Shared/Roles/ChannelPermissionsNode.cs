using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Roles
{
    /// <summary>
    /// A permission node is a set of permissions for a specific thing
    /// This is a set of permissions for a specific channel
    /// </summary>
    class ChannelPermissionsNode
    {
        /// <summary>
        /// The permission code that this node has set
        /// </summary>
        public ulong Code { get; set; }
    }
}
