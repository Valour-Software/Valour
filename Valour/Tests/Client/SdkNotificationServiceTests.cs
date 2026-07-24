using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class SdkNotificationServiceTests
{
    [Fact]
    public void OnNotificationReceived_DuplicateUnreadId_UpdatesInPlaceWithoutDuplicating()
    {
        // Coalesced notifications (channel activity) re-relay the same id with
        // updated content: the list must not gain a duplicate entry, but the
        // event must still fire so the inbox re-renders the new content
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        var notificationId = Guid.NewGuid();
        var received = 0;
        service.NotificationReceived += _ => received++;

        service.OnNotificationReceived(CreateNotification(client, notificationId));

        var updated = CreateNotification(client, notificationId);
        updated.Body = "Updated body";
        service.OnNotificationReceived(updated);

        Assert.Equal(2, received);
        var entry = Assert.Single(service.UnreadNotifications);
        Assert.Equal("Updated body", entry.Body);
    }

    [Fact]
    public void OnNotificationReceived_ReadThenUnread_RaisesEachStateChange()
    {
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        var notificationId = Guid.NewGuid();
        var received = 0;
        service.NotificationReceived += _ => received++;

        service.OnNotificationReceived(CreateNotification(client, notificationId));
        service.OnNotificationReceived(CreateNotification(client, notificationId, DateTime.UtcNow));
        service.OnNotificationReceived(CreateNotification(client, notificationId));

        Assert.Equal(3, received);
        Assert.Single(service.UnreadNotifications);
    }

    private static Notification CreateNotification(
        ValourClient client,
        Guid id,
        DateTime? timeRead = null) => new(client)
    {
        Id = id,
        Title = "Reply",
        Body = "Message",
        ImageUrl = "",
        ClickUrl = "",
        TimeRead = timeRead,
        TimeSent = DateTime.UtcNow,
    };
}
