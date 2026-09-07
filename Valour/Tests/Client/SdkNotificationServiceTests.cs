using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class SdkNotificationServiceTests
{
    [Fact]
    public void OnNotificationReceived_DuplicateUnreadId_UpdatesInPlaceWithoutDuplicating()
    {
        // Coalesced notifications (channel activity) re-relay the same id with
        // updated content: the list must not gain a duplicate entry, and only
        // NotificationContentUpdated fires — NotificationReceived would replay
        // sounds and popups for what is just a content refresh
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        var notificationId = Guid.NewGuid();
        var received = 0;
        var updatedCount = 0;
        service.NotificationReceived += _ => received++;
        service.NotificationContentUpdated += _ => updatedCount++;

        service.OnNotificationReceived(CreateNotification(client, notificationId));

        var updated = CreateNotification(client, notificationId);
        updated.Body = "Updated body";
        service.OnNotificationReceived(updated);

        Assert.Equal(1, received);
        Assert.Equal(1, updatedCount);
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

    [Fact]
    public void UnreadSnapshots_RemainEnumerableDuringIncomingUpdatesAndClears()
    {
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        service.OnNotificationReceived(CreateNotification(client, Guid.NewGuid()));
        var snapshot = service.GetUnreadInternal();
        Parallel.For(0, 200, i =>
        {
            service.OnNotificationReceived(CreateNotification(client, Guid.NewGuid()));
            foreach (var entry in service.UnreadNotifications) Assert.NotEqual(Guid.Empty, entry.Id);
            if (i % 10 == 0) service.OnNotificationsCleared();
        });
        Assert.Single(snapshot);
    }

    [Fact]
    public void SourceChange_RemovesPreviousLookupWithoutRemovingAnotherNotification()
    {
        var client = new ValourClient("https://api.valour.example/");
        var service = client.NotificationService;
        var id = Guid.NewGuid();
        var first = CreateNotification(client, id);
        first.SourceId = 123;
        service.OnNotificationReceived(first);
        var snapshot = service.UnreadNotificationsLookupBySource;
        var changed = CreateNotification(client, id);
        changed.SourceId = 456;
        service.OnNotificationReceived(changed);
        Assert.True(snapshot.ContainsKey(123));
        Assert.False(service.UnreadNotificationsLookupBySource.ContainsKey(123));
        Assert.Equal(id, service.UnreadNotificationsLookupBySource[456].Id);
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
