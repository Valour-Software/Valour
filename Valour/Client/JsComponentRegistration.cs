using Microsoft.AspNetCore.Components.Web;
using Valour.Client.Components.Windows.ChannelWindows;

namespace Valour.Client;

/// <summary>
/// Registers the components that client scripts add to the page themselves.
/// Every host (browser, MAUI, and Photino) must call this on its root
/// component configuration.
/// </summary>
public static class JsComponentRegistration
{
    public static void RegisterValourJsComponents(this IJSComponentConfiguration configuration)
    {
        configuration.RegisterForJavaScript<InputMentionChip>(InputMentionChip.JsIdentifier);
    }
}
