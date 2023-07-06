using Valour.Api.Models;
using Valour.Client.Components.Windows.ChannelWindows.PlanetChatChannels;

namespace Valour.Client.Windows.ChatWindows;
public class PlanetChatChannelWindow : ChatChannelWindow, IPlanetWindow
{

    public Planet Planet { get; set; }
    public PlanetChatChannel PlanetChannel { get; set; }
    public PlanetChatChannelWindowComponent PlanetWindowComponent { get; set; }

    public PlanetChatChannelWindow(Planet planet, PlanetChatChannel channel) : base(channel)
    {
        PlanetChannel = channel;
        Planet = planet;
    }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(PlanetChatChannelWindowComponent);
}
