using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Menus.Modals;
using Valour.Sdk.Models;
using Valour.Client.Components.Menus.Modals.Users.Edit;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public static class NotificationNavigator
{
    public static async Task NavigateTo(Notification notification)
    {
        var client = notification.Client;
        
        switch (notification.Source)
        {
            case NotificationSource.PlanetMemberMention:
            case NotificationSource.PlanetRoleMention:
            case NotificationSource.PlanetMemberReply:
            {
                var planet = await client.PlanetService.FetchPlanetAsync(notification.PlanetId!.Value);
                if (planet is null)
                    break;

                var channel = planet.Channels.FirstOrDefault(x => x.Id == notification.ChannelId);
                if (channel is null)
                    break;

                var content = await ChatWindowComponent.GetDefaultContent(channel);
                await WindowService.OpenWindowAtFocused(content);

                break;
            }
            case NotificationSource.DirectMention:
            case NotificationSource.DirectReply:
            {
                var channel = await client.ChannelService.FetchDirectChannelAsync(notification.ChannelId!.Value);
                if (channel is null)
                    break;
                
                var content = await ChatWindowComponent.GetDefaultContent(channel);
                await WindowService.OpenWindowAtFocused(content);
                
                break;
            }
            case NotificationSource.FriendRequest:
            {
                var data = new EditUserComponent.ModalParams()
                {
                    StartTopMenu = "General Settings",
                    StartSubMenu = "Friends",
                    User = client.Me
                };
                
                ModalInjector.Service.OpenModal<EditUserComponent>(data);

                break;
            }
        }
    }
}