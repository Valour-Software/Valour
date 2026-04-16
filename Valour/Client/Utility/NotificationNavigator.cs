using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Menus.Modals;
using Valour.Sdk.Models;
using Valour.Client.Components.Menus.Modals.Users.Edit;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Toast;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public static class NotificationNavigator
{
    private static void ShowNavigationFailureToast(string message)
    {
        ToastContainer.Instance?.AddToast(new ToastData(
            "Couldn't Open Notification",
            message,
            ToastProgressState.Failure));
    }

    public static async Task NavigateTo(Notification notification)
    {
        if (notification?.Client is null)
        {
            ShowNavigationFailureToast("Notification data is unavailable.");
            return;
        }

        var client = notification.Client;

        try
        {
            switch (notification.Source)
            {
                case NotificationSource.PlanetMemberMention:
                case NotificationSource.PlanetRoleMention:
                case NotificationSource.PlanetMemberReply:
                case NotificationSource.PlanetHereMention:
                case NotificationSource.PlanetEveryoneMention:
                {
                    if (notification.PlanetId is null || notification.ChannelId is null)
                    {
                        ShowNavigationFailureToast("This planet notification is missing route details.");
                        return;
                    }

                    var planet = await client.PlanetService.FetchPlanetAsync(notification.PlanetId.Value);
                    if (planet is null)
                    {
                        ShowNavigationFailureToast("Couldn't load the planet for this notification.");
                        return;
                    }

                    var channel = await client.ChannelService.FetchPlanetChannelAsync(
                        notification.ChannelId.Value, planet);
                    if (channel is null)
                    {
                        ShowNavigationFailureToast("Couldn't load the channel for this notification.");
                        return;
                    }

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
                    {
                        ShowNavigationFailureToast("This DM notification is missing channel details.");
                        return;
                    }

                    var channel = await client.ChannelService.FetchDirectChannelAsync(notification.ChannelId.Value);
                    if (channel is null)
                    {
                        ShowNavigationFailureToast("Couldn't load the direct message channel.");
                        return;
                    }

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
                default:
                    ShowNavigationFailureToast("This notification doesn't have a destination.");
                    break;
            }
        }
        catch (Exception ex)
        {
            client.Logger?.Log("NotificationNavigator", $"Failed to open notification {notification.Id}: {ex}", "red");
            ShowNavigationFailureToast("Couldn't open this notification. Check your connection and try again.");
        }
    }
}
