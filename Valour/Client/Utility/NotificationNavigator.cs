using Blazored.Modal;
using Blazored.Modal.Services;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Client.Components.Menus.Modals.Users.Edit;
using Valour.Client.Windows;
using Valour.Client.Windows.ChatWindows;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public static class NotificationNavigator
{
    public static async Task NavigateTo(Notification notification, IModalService modalService)
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
            case NotificationSource.FriendRequest:
            {
                var param = new ModalParameters();
                param.Add("StartTopMenu", "General Settings");
                param.Add("StartSubMenu", "Friends");
                modalService.Show<EditUserComponent>("Edit User", param);

                break;
            }
        }
    }
}