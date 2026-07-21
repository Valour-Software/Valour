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
}
