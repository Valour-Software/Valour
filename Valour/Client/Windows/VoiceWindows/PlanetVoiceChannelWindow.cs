using Valour.Api.Items.Channels.Planets;
using Valour.Client.Components.Windows.ChannelWindows.PlanetVoiceChannels;
using Valour.Client.Windows.ChatWindows;

namespace Valour.Client.Windows.VoiceWindows;

public class PlanetVoiceChannelWindow : VoiceChannelWindow
{
    public PlanetVoiceChannel PlanetChannel { get; set; }
    public PlanetVoiceChannelWindowComponent PlanetWindowComponent { get; set; }

    public PlanetVoiceChannelWindow(PlanetVoiceChannel channel) : base(channel)
    {
        PlanetChannel = channel;
    }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(PlanetVoiceChannelWindowComponent);
}
