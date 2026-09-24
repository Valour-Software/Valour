using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Server.Services;
using Valour.Shared.Models;
using DbSubscription = Valour.Database.PushNotificationSubscription;

namespace Valour.Tests.Services;

public class ReleasePushDeliveryTests
{
    [Fact]
    public async Task Delivery_UsesAes128GcmAndSignedCachedVapidToken()
    {
        var handler = new Handler();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = Create(handler, key);
        var subscription = Subscription("https://web.push.apple.com/test");
        await delivery.SendAsync(subscription, "payload", CancellationToken.None);
        await delivery.SendAsync(subscription, "payload", CancellationToken.None);
        var request = handler.Requests.ToArray()[0];
        Assert.Equal("aes128gcm", request.Encoding);
        Assert.Equal("vapid", request.Scheme);
        Assert.Equal(handler.Requests.ToArray()[0].Authorization, handler.Requests.ToArray()[1].Authorization);
        Assert.True(request.BodyLength > 86);
        var token = request.Authorization.Split(',')[0].Trim()[2..];
        var segments = token.Split('.');
        using var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
        Assert.Equal("https://web.push.apple.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal("mailto:push@example.com", claims.RootElement.GetProperty("sub").GetString());
        Assert.InRange(claims.RootElement.GetProperty("exp").GetInt64(), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]),
            WebEncoders.Base64UrlDecode(segments[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task ConcurrentDelivery_ReusesOneTokenPerProvider()
    {
        var handler = new Handler();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = Create(handler, key);
        var subscription = Subscription("https://web.push.apple.com/test");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await delivery.SendAsync(subscription, "payload", CancellationToken.None);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(sends);
        Assert.Equal(32, handler.Delivered);
        Assert.Single(handler.Requests.Select(x => x.Authorization).Distinct());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneFailedEndpoint_DoesNotCancelHealthyRecipients(bool timeout)
    {
        var handler = new Handler { Failure = timeout ? Failure.Timeout : Failure.Transport };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = Create(handler, key);
        var service = new PushNotificationService(NullLogger<PushNotificationService>.Instance, null!, null!, null!, delivery);
        var subscriptions = Enumerable.Range(0, 32).Select(i => Subscription($"https://push.example.com/{(i == 0 ? "failure" : i.ToString())}")).ToArray();
        await service.SendParallelNotificationsAsync(subscriptions, "payload");
        Assert.Equal(32, handler.Requests.Count);
        Assert.Equal(31, handler.Delivered);
        // No database was supplied: a transient failure must not attempt to delete a subscription.
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var handler = new Handler();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = Create(handler, key);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery.SendAsync(Subscription("https://push.example.com/test"), "payload", cancelled.Token));
    }

    [Fact]
    public async Task ProviderForbidden_DoesNotDeleteSubscriptionOrCancelOtherRecipients()
    {
        var handler = new Handler { Failure = Failure.Forbidden };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = Create(handler, key);
        var service = new PushNotificationService(NullLogger<PushNotificationService>.Instance, null!, null!, null!, delivery);
        await service.SendParallelNotificationsAsync([Subscription("https://push.example.com/failure"), Subscription("https://push.example.com/healthy")], "payload");
        Assert.Equal(1, handler.Delivered);
    }

    internal static WebPushDeliveryClient Create(Handler handler, ECDsa key)
    {
        var original = NotificationsConfig.Current;
        try
        {
            var parameters = key.ExportParameters(true);
            return new(new HttpClient(handler), new NotificationsConfig
            {
                Subject = "mailto:push@example.com",
                PublicKey = WebEncoders.Base64UrlEncode([4, .. parameters.Q.X!, .. parameters.Q.Y!]),
                PrivateKey = WebEncoders.Base64UrlEncode(parameters.D!)
            });
        }
        finally { NotificationsConfig.Current = original; }
    }

    internal static DbSubscription Subscription(string endpoint)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        return new()
        {
            Endpoint = endpoint, DeviceType = NotificationDeviceType.WebPush,
            Key = WebEncoders.Base64UrlEncode([4, .. parameters.Q.X!, .. parameters.Q.Y!]),
            Auth = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))
        };
    }

    internal enum Failure { None, Timeout, Transport, Forbidden, Gone }
    internal sealed class Handler : HttpMessageHandler
    {
        public Failure Failure;
        public int Delivered;
        public System.Collections.Concurrent.ConcurrentBag<(string Scheme, string Authorization, string Encoding, int BodyLength)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter!, request.Content.Headers.ContentEncoding.Single(), body.Length));
            if (request.RequestUri!.AbsolutePath == "/failure")
            {
                if (Failure == Failure.Timeout) throw new TaskCanceledException("Simulated provider timeout");
                if (Failure == Failure.Transport) throw new HttpRequestException("Simulated connection failure");
                if (Failure == Failure.Forbidden) return new(HttpStatusCode.Forbidden) { Content = new StringContent("BadJwtToken") };
                if (Failure == Failure.Gone) return new(HttpStatusCode.Gone);
            }
            Interlocked.Increment(ref Delivered);
            return new(HttpStatusCode.Created);
        }
    }
}
