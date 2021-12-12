using Valour.Api.Items.Planets.Channels;
using Valour.Client.Shared.Windows.ChannelWindows;

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
    public ChatChannelWindow(int index, PlanetChatChannel channel) : base(index)
    {
        this.Channel = channel;
    }

    public override void OnClosed()
    {
        // Must be after SetChannelWindowClosed
        base.OnClosed();

        Task.Run(async () =>
        {
            await Component.OnWindowClosed();
        });
    }
}
