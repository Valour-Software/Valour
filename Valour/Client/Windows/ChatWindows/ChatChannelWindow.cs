using Valour.Api.Items.Channels;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;

public abstract class ChatChannelWindow : ClientWindow 
{

    /// <summary>
    /// The channel for this chat window
    /// </summary>
    public IChatChannel Channel { get; set; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChatChannelWindowComponent Component { get; private set; }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(ChatChannelWindowComponent);

    /// <summary>
    /// Sets the component of this window
    /// </summary>
    public void SetComponent(ChatChannelWindowComponent newComponent)
    {
        Component = newComponent;
    }

    public ChatChannelWindow(IChatChannel channel)
    {
        Channel = channel;
    }

    public override async Task OnClosedAsync()
    {
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
