using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Channels;

public class ChannelTypingUpdate
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
}
