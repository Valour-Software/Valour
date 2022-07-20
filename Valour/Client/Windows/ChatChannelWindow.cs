using Valour.Api.Items.Planets.Channels;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows;

public class ChatChannelWindow : ClientWindow
{
    /// <summary>
    /// The channel this window represents
    /// </summary>
    public PlanetChatChannel Channel { get; set; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChannelWindowComponent Component { get; set; }

    public override Type GetComponentType() =>
        typeof(ChannelWindowComponent);
    public ChatChannelWindow(PlanetChatChannel channel)
    {
        this.Channel = channel;
    }

    public override async Task OnClosedAsync()
    {
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
