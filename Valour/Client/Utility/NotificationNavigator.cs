using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Menus.Modals;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Client.Components.Menus.Modals.Users.Edit;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public static class NotificationNavigator
{
    public static async Task NavigateTo(Notification notification)
    {
        switch (notification.Source)
        {
            case NotificationSource.PlanetMemberMention:
            case NotificationSource.PlanetRoleMention:
            case NotificationSource.PlanetMemberReply:
            {
                var planet = ValourCache.Get<Planet>(notification.PlanetId);
                if (planet is null)
                    break;

                var channel = planet.ChatChannels.FirstOrDefault(x => x.Id == notification.ChannelId);
                if (channel is null)
                    break;

                var content = await ChatChannelWindowComponent.GetDefaultContent(channel);
                await WindowService.OpenWindowAtFocused(content);

                break;
            }
            case NotificationSource.DirectMention:
            case NotificationSource.DirectReply:
            {
                var channel = ValourCache.Get<Channel>(notification.ChannelId);
                if (channel is null)
                    break;
                
                var content = await ChatChannelWindowComponent.GetDefaultContent(channel);
                await WindowService.OpenWindowAtFocused(content);
                
                break;
            }
            case NotificationSource.FriendRequest:
            {
                var data = new EditUserComponent.ModalParams()
                {
                    StartTopMenu = "General Settings",
                    StartSubMenu = "Friends",
                    User = ValourClient.Self
                };
                
                ModalInjector.Service.OpenModal<EditUserComponent>(data);

                break;
            }
        }
    }
}