using Valour.Api.Models;
using Valour.Client.Components.Windows.ChannelWindows.Voice;

namespace Valour.Client.Windows.ChatWindows;
public class VoiceChannelWindow : ClientWindow
{

    /// <summary>
    /// The channel for this voice window
    /// </summary>
    public IVoiceChannel Channel { get; set; }

    /// <summary>
    /// The component that belongs to this window
    /// </summary>
    public VoiceChannelWindowComponent Component { get; private set; }

    /// <summary>
    /// Returns the type for the component of this window
    /// </summary>
    public override Type GetComponentType() =>
        typeof(VoiceChannelWindowComponent);

    /// <summary>
    /// Sets the component of this window
    /// </summary>
    public void SetComponent(VoiceChannelWindowComponent newComponent)
    {
        Component = newComponent;
    }

    public VoiceChannelWindow(IVoiceChannel channel)
    {
        Channel = channel;
    }

    public override async Task OnClosedAsync()
    {
        await base.OnClosedAsync();
        await Component.OnWindowClosed();
    }
}
