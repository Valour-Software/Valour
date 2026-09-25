using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

public class PushSubscriptionPolicyTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:APA91b")]
    [InlineData("https://fcm.googleapis.com/wp/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://web.push.apple.com/QGx3")]
    [InlineData("https://wns2-by3p.notify.windows.com/w/?token=abc")]
    public void BrowserPushServices_AreAccepted(string endpoint)
    {
        Assert.True(PushSubscriptionPolicy.IsAllowedWebPushEndpoint(endpoint));
        Assert.Null(PushSubscriptionPolicy.Validate(WebPush(endpoint)));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]
    [InlineData("https://user@fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://localhost/push")]
    [InlineData("https://fcm.googleapis.com.evil.example/push")]
    [InlineData("https://notify.windows.com.evil.example/push")]
    [InlineData("not a url")]
    [InlineData("")]
    public void OtherEndpoints_AreRejected(string endpoint)
    {
        Assert.False(PushSubscriptionPolicy.IsAllowedWebPushEndpoint(endpoint));
        Assert.NotNull(PushSubscriptionPolicy.Validate(WebPush(endpoint)));
    }

    [Fact]
    public void WebPushWithoutKeys_IsRejected()
    {
        var subscription = WebPush("https://fcm.googleapis.com/fcm/send/abc");
        subscription.Auth = "";
        Assert.NotNull(PushSubscriptionPolicy.Validate(subscription));
    }

    [Theory]
    [InlineData("dXk3bm9fOjE:APA91bH-ab_cd", true)]
    [InlineData("https://internal.example/push", false)]
    [InlineData("", false)]
    public void FcmTokens_MustBeOpaqueTokens(string token, bool valid)
    {
        var subscription = new Valour.Server.Models.PushNotificationSubscription
        {
            DeviceType = NotificationDeviceType.AndroidFcm,
            Endpoint = token,
            Key = "",
            Auth = ""
        };

        Assert.Equal(valid, PushSubscriptionPolicy.Validate(subscription) is null);
    }

    [Fact]
    public void SameDevice_RequiresMatchingWebPushKeys()
    {
        var existing = WebPush("https://fcm.googleapis.com/fcm/send/abc");
        var sameDevice = WebPush(existing.Endpoint);
        var otherKeys = WebPush(existing.Endpoint);
        otherKeys.Auth = "ZGlmZmVyZW50YXV0aA";

        Assert.True(PushSubscriptionPolicy.IsSameDeviceSubscription(existing, sameDevice));
        Assert.False(PushSubscriptionPolicy.IsSameDeviceSubscription(existing, otherKeys));
    }

    private static Valour.Server.Models.PushNotificationSubscription WebPush(string endpoint) => new()
    {
        DeviceType = NotificationDeviceType.WebPush,
        Endpoint = endpoint,
        Key = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM",
        Auth = "tBHItJI5svbpez7KI4CCXg"
    };
}
