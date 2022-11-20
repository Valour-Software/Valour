using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Channels;

public class ChannelTypingUpdate
{
    public long ChannelId { get; set; }

    /// <summary>
    /// List of user ids who are currently typing in this channel
    /// </summary>
    public List<long> UserIds { get; set; }
}
