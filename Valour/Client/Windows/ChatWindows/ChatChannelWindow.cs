using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client.Windows.ChatWindows;

public class ChatChannelWindow : ClientWindow
{
    private readonly string _lockKey = Guid.NewGuid().ToString();

    /// <summary>
    /// The channel for this chat window
    /// </summary>
    public Channel Channel { get; set; }

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

    public ChatChannelWindow(Channel channel)
    {
        Channel = channel;

        if (channel.PlanetId is not null)
        {
            ValourClient.AddPlanetLock(_lockKey, channel.PlanetId.Value);
        }
    }

    public override async Task OnClosedAsync()
    {
        if (Channel.PlanetId is not null)
        {
            await ValourClient.RemovePlanetLock(_lockKey);
        }
        
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
