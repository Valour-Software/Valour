using Valour.Api.Items.Channels.Planets;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;
public class PlanetChatChannelWindow : ChatChannelWindow<PlanetChatChannel, ChannelWindowComponent>
{
    public PlanetChatChannelWindow(PlanetChatChannel channel, ChannelWindowComponent component) : base(channel, component)
    {
    }
}
