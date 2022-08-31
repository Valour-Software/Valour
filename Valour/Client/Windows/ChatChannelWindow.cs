using Valour.Api.Items.Channels;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows;

public class ChatChannelWindow : ClientWindow
{
    /// <summary>
    /// The channel this window represents
    /// </summary>
    public IChatChannel Channel { get; set; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChannelWindowComponent Component { get; set; }

    public override Type GetComponentType() =>
        typeof(ChannelWindowComponent);
    public ChatChannelWindow(IChatChannel channel)
    {
        this.Channel = channel;
    }

    public override async Task OnClosedAsync()
    {
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
