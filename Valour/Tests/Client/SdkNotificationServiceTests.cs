using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class SdkNotificationServiceTests
{
    [Fact]
    public void OnNotificationReceived_DuplicateUnreadId_RaisesOneEvent()
    {
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        var notificationId = Guid.NewGuid();
        var received = 0;
        service.NotificationReceived += _ => received++;

        service.OnNotificationReceived(CreateNotification(client, notificationId));
        service.OnNotificationReceived(CreateNotification(client, notificationId));

        Assert.Equal(1, received);
        Assert.Single(service.UnreadNotifications);
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
