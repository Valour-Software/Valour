using Valour.Api.Items.Channels;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;

public abstract class ChatChannelWindow : ClientWindow 
{

    /// <summary>
    /// The channel for this chat window
    /// </summary>
    public IChatChannel Channel { get; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChannelWindowComponent Component { get; private set; }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(ChannelWindowComponent);

    /// <summary>
    /// Sets the component of this window
    /// </summary>
    public void SetComponent(ChannelWindowComponent newComponent)
    {
        Component = newComponent;
    }

    public ChatChannelWindow(IChatChannel channel, ChannelWindowComponent component)
    {
        Channel = channel;
        Component = component;
    }

    public override async Task OnClosedAsync()
    {
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
