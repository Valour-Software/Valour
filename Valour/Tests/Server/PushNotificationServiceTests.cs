using System.Net;
using System.Text.Json;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

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

    [Fact]
    public void GetPayload_CarriesTheEnvelopeWhenItFits()
    {
        var envelope = new byte[600];
        Random.Shared.NextBytes(envelope);

        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = NotificationService.EncryptedMessageBody,
            PlanetId = long.MaxValue,
            ChannelId = long.MaxValue - 1,
            Envelope = envelope,
        });

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        Assert.Equal(Convert.ToBase64String(envelope), root.GetProperty("envelope").GetString());
        Assert.Equal(long.MaxValue.ToString(), root.GetProperty("planetId").GetString());
        Assert.Equal((long.MaxValue - 1).ToString(), root.GetProperty("channelId").GetString());
    }

    [Fact]
    public void GetPayload_LeavesOutAnEnvelopeThatWouldPassTheLimit()
    {
        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = NotificationService.EncryptedMessageBody,
            ChannelId = 5,
            Envelope = new byte[PushNotificationService.MaxPayloadBytes],
        });

        using var json = JsonDocument.Parse(payload);

        Assert.False(json.RootElement.TryGetProperty("envelope", out _));
        Assert.Equal("5", json.RootElement.GetProperty("channelId").GetString());
        Assert.Equal(NotificationService.EncryptedMessageBody, json.RootElement.GetProperty("message").GetString());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= PushNotificationService.MaxPayloadBytes);
    }

    [Fact]
    public void BuildFcmMessage_SendsDataMessagesToAppsThatDecrypt()
    {
        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = NotificationService.EncryptedMessageBody,
            IconUrl = "/media/avatar.png",
            Url = "/directchannels/5/6",
            NotificationId = Guid.NewGuid(),
            SourceId = 6,
            ChannelId = 5,
            Envelope = [1, 2, 3],
        });

        var message = PushNotificationService.BuildFcmMessage("token", NotificationDeviceType.AndroidFcmData, payload,
            "https://app.valour.gg");

        Assert.Null(message.Notification);
        Assert.Equal(FirebaseAdmin.Messaging.Priority.High, message.Android.Priority);
        Assert.Equal("Title", message.Data["title"]);
        Assert.Equal(NotificationService.EncryptedMessageBody, message.Data["message"]);
        Assert.Equal("https://app.valour.gg/media/avatar.png", message.Data["iconUrl"]);
        Assert.Equal("/directchannels/5/6", message.Data["url"]);
        Assert.Equal("6", message.Data["sourceId"]);
        Assert.Equal("5", message.Data["channelId"]);
        Assert.Equal("AQID", message.Data["envelope"]);
        Assert.False(message.Data.ContainsKey("planetId"));
    }

    [Fact]
    public void BuildFcmMessage_KeepsNotificationMessagesForOlderApps()
    {
        var payload = PushNotificationService.GetPayload(new NotificationContent
        {
            Title = "Title",
            Message = NotificationService.EncryptedMessageBody,
            Url = "/directchannels/5/6",
            ChannelId = 5,
            Envelope = [1, 2, 3],
        });

        var message = PushNotificationService.BuildFcmMessage("token", NotificationDeviceType.AndroidFcm, payload,
            "https://app.valour.gg");

        Assert.Equal("Title", message.Notification.Title);
        Assert.Equal(NotificationService.EncryptedMessageBody, message.Notification.Body);
        Assert.Equal("valour_default", message.Android.Notification.ChannelId);
        Assert.Equal(["url"], message.Data.Keys);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, null, PushNotificationService.WebPushFailure.SubscriptionGone)]
    [InlineData(HttpStatusCode.NotFound, null, PushNotificationService.WebPushFailure.SubscriptionGone)]
    [InlineData(HttpStatusCode.Forbidden, "{\"reason\":\"BadJwtToken\"}", PushNotificationService.WebPushFailure.SubscriptionRejected)]
    [InlineData(HttpStatusCode.InternalServerError, "permanent internal error encountered, do not retry the request.", PushNotificationService.WebPushFailure.SubscriptionRejected)]
    [InlineData(HttpStatusCode.InternalServerError, "transient internal error encountered, retry the request with exponential backoff.", PushNotificationService.WebPushFailure.Transient)]
    [InlineData(HttpStatusCode.BadGateway, null, PushNotificationService.WebPushFailure.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, null, PushNotificationService.WebPushFailure.Transient)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, null, PushNotificationService.WebPushFailure.Unexpected)]
    public void ClassifyWebPushFailure_SortsProviderResponses(HttpStatusCode status, string? body,
        PushNotificationService.WebPushFailure expected)
    {
        Assert.Equal(expected, PushNotificationService.ClassifyWebPushFailure(status, body));
    }
}
