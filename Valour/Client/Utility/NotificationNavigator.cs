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
            case NotificationSource.PlanetHereMention:
            case NotificationSource.PlanetEveryoneMention:
            {
                if (notification.PlanetId is null || notification.ChannelId is null)
                    break;

                var planet = await client.PlanetService.FetchPlanetAsync(notification.PlanetId.Value);
                if (planet is null)
                    break;

                var channel = planet.Channels.FirstOrDefault(x => x.Id == notification.ChannelId);
                if (channel is null)
                    break;

                var content = await ChatWindowComponent.GetDefaultContent(channel);

                // Navigate to the specific message if available
                if (notification.SourceId is not null)
                    content.TargetMessageId = notification.SourceId;

                await WindowService.OpenWindowAtFocused(content);

                break;
            }
            case NotificationSource.DirectMention:
            case NotificationSource.DirectReply:
            case NotificationSource.DirectMessage:
            {
                if (notification.ChannelId is null)
                    break;

                var channel = await client.ChannelService.FetchDirectChannelAsync(notification.ChannelId.Value);
                if (channel is null)
                    break;

                var content = await ChatWindowComponent.GetDefaultContent(channel);

                // Navigate to the specific message if available
                if (notification.SourceId is not null)
                    content.TargetMessageId = notification.SourceId;

                await WindowService.OpenWindowAtFocused(content);

                break;
            }
            case NotificationSource.FriendRequest:
            case NotificationSource.FriendRequestAccepted:
            {
                var data = new EditUserComponent.ModalParams()
                {
                    StartCategory = "General Settings",
                    StartItem = "Friends",
                    User = client.Me
                };

                ModalInjector.Service.OpenModal<EditUserComponent>(data);

                break;
            }
            // Economy notifications (TransactionReceived, TradeProposed, TradeAccepted, TradeDeclined)
            // and Platform notifications don't have a specific navigation target
        }
    }
}