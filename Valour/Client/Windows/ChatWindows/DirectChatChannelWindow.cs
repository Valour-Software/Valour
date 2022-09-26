using Valour.Api.Items.Channels.Users;
using Valour.Client.Components.Windows.ChannelWindows.DirectChatChannels;

namespace Valour.Client.Windows.ChatWindows;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class DirectChatChannelWindow : ChatChannelWindow
{
    public DirectChatChannel DirectChannel { get; set; }
    public DirectChatChannelWindowComponent DirectWindowComponent { get; set; }

    public DirectChatChannelWindow(DirectChatChannel channel) : base(channel)
    {
        DirectChannel = channel;
    }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(DirectChatChannelWindowComponent);
    
}
