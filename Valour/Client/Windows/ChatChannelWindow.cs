using Valour.Api.Planets;
using Valour.Client.Shared.Windows.PlanetChannelWindow;

namespace Valour.Client.Windows;

public class ChatChannelWindow : ClientWindow
{
    /// <summary>
    /// The channel this window represents
    /// </summary>
    public Channel Channel { get; set; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChannelWindowComponent Component { get; set; }

    public ChatChannelWindow(int index, Channel channel) : base(index)
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
