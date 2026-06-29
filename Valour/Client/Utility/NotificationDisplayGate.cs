using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Utility;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Models;

namespace Valour.Client.Utility;

public static class NotificationDisplayGate
{
    public static bool ShouldSuppressLocalNotification(Notification notification)
    {
        if (notification.ChannelId is not long channelId || !BrowserUtils.IsFocused)
            return false;

        return WindowService.GlobalTabs.Any(tab =>
            tab.Content is ChatWindowComponent.Content { Data: not null } content &&
            content.Data.Id == channelId &&
            IsTabVisible(tab));
    }

    private static bool IsTabVisible(WindowTab tab)
    {
        if (tab.IsFloating)
            return true;

        return tab.Layout?.FocusedTab == tab;
    }
}
