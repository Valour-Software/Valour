using System.Text.Json;
using Valour.Server.Models;
using Valour.Server.Services;

namespace Valour.Tests.Server;

public class PushNotificationServiceTests
{
    [Fact]
    public void GetPayload_PreservesIdentifiersAsStrings()
    {
        var notificationId = Guid.NewGuid();
        const long sourceId = long.MaxValue;

        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = "Message",
            NotificationId = notificationId,
            SourceId = sourceId,
        });

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        Assert.Equal(notificationId.ToString(), root.GetProperty("notificationId").GetString());
        Assert.Equal(sourceId.ToString(), root.GetProperty("sourceId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("iconUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("url").ValueKind);
    }

    [Fact]
    public void GetPayload_IncludesTimeSentAsUnixMilliseconds()
    {
        var timeSent = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);

        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = "Message",
            TimeSent = timeSent,
        });

        using var json = JsonDocument.Parse(payload);
        var actual = json.RootElement.GetProperty("timeSent").GetInt64();

        Assert.Equal(new DateTimeOffset(timeSent).ToUnixTimeMilliseconds(), actual);
    }

    [Fact]
    public void GetPayload_UnsetTimeSent_FallsBackToNow()
    {
        // A default TimeSent must never leak through as year-0001: Android
        // shells render that as a bogus "1/2/01" date on the notification card
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = "Message",
        });

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var json = JsonDocument.Parse(payload);
        var actual = json.RootElement.GetProperty("timeSent").GetInt64();

        Assert.InRange(actual, before, after);
    }
}
