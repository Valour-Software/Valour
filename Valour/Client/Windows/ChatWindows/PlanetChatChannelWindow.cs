using Valour.Api.Models;
using Valour.Client.Components.Windows.ChannelWindows.PlanetChatChannels;

namespace Valour.Client.Windows.ChatWindows;
public class PlanetChatChannelWindow : ChatChannelWindow
{

    public PlanetChatChannel PlanetChannel { get; set; }
    public PlanetChatChannelWindowComponent PlanetWindowComponent { get; set; }

    public PlanetChatChannelWindow(PlanetChatChannel channel) : base(channel)
    {
        PlanetChannel = channel;
    }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(PlanetChatChannelWindowComponent);
}
