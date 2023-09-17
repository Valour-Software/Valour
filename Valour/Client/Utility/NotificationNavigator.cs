using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Client.Windows;
using Valour.Client.Windows.ChatWindows;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public static class NotificationNavigator
{
    public static async Task NavigateTo(Notification notification)
    {
        var windowManager = WindowManager.Instance;
        
        switch (notification.Source)
        {
            case NotificationSource.PlanetMemberMention:
            case NotificationSource.PlanetRoleMention:
            case NotificationSource.PlanetMemberReply:
            {
                var planet = ValourCache.Get<Planet>(notification.PlanetId);
                if (planet is null)
                    break;

                var channel = (await planet.GetChannelsAsync()).FirstOrDefault(x => x.Id == notification.ChannelId);
                if (channel is null)
                    break;
						
                await ValourClient.OpenPlanet(planet);
                await windowManager.SetFocusedPlanet(planet);

                var selectedWindow = windowManager.GetSelectedWindow();
                await windowManager.ReplaceWindow(selectedWindow, new PlanetChatChannelWindow(planet, channel));

                break;
            }
            case NotificationSource.DirectMention:
            case NotificationSource.DirectReply:
            {
                var channel = ValourCache.Get<DirectChatChannel>(notification.ChannelId);
                if (channel is null)
                    break;
						
                var selectedWindow = windowManager.GetSelectedWindow();
                await windowManager.ReplaceWindow(selectedWindow, new DirectChatChannelWindow(channel));
                
                break;
            }
        }
    }
}