using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Roles
{
    public class ChannelPermissionsNodeResponse
    {
        public bool Exists { get; set; }
        public ChannelPermissionsNode Node { get; set; }
    }
}
