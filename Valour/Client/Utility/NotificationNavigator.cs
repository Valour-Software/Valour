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

                var channel = (await planet.GetChatChannelsAsync()).FirstOrDefault(x => x.Id == notification.ChannelId);
                if (channel is null)
                    break;
                
                await DockContainer.MainDock.AddWindowAsync(new WindowData()
                    {
                        Title = await channel.GetTitleAsync(),
                        Data = channel,
                        Type = typeof(ChatChannelWindowComponent),
                        Icon = planet.IconUrl
                    }
                );

                break;
            }
            case NotificationSource.DirectMention:
            case NotificationSource.DirectReply:
            {
                var channel = ValourCache.Get<Channel>(notification.ChannelId);
                if (channel is null)
                    break;
                
                await DockContainer.MainDock.AddWindowAsync(new WindowData()
                    {
                        Title = await channel.GetTitleAsync(),
                        Data = channel,
                        Type = typeof(ChatChannelWindowComponent),
                        Icon = await channel.GetIconAsync()
                    }
                );
                
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