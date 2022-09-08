using Valour.Api.Items.Channels;
using Valour.Api.Items.Channels.Planets;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;

// Generics format is <ChannelType, ChannelComponentType>
public abstract class ChatChannelWindow<T> : ClientWindow 
    where T : IChatChannel 
{

    /// <summary>
    /// The channel for this chat window
    /// </summary>
    public T Channel { get; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public ChannelWindowComponent<T> Component { get; private set; }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(ChannelWindowComponent<T>);

    /// <summary>
    /// Sets the component of this window
    /// </summary>
    public void SetComponent(ChannelWindowComponent<T> newComponent)
    {
        Component = newComponent;
    }

    public ChatChannelWindow(T channel, ChannelWindowComponent<T> component)
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
