using Valour.Api.Items.Channels.Planets;
using Valour.Api.Items.Messages;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;
public class PlanetChatChannelWindow : ChatChannelWindow<PlanetChatChannel, PlanetMessage>
{
    public PlanetChatChannelWindow(PlanetChatChannel channel, ChannelWindowComponent<PlanetChatChannel, PlanetMessage> component) : base(channel, component)
    {
    }
}
