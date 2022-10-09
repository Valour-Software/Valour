using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Channels
{
    public class ChannelWatchingUpdate
    {
        public long ChannelId { get; set; }
        public List<long> UserIds { get; set; }
    }
}
