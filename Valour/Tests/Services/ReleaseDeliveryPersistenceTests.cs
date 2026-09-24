using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class ReleaseDeliveryPersistenceTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task FailedKick_PersistsOneFailureAndOnlyClosesAfterRecovery()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        _ = new CloudflareConfig { RealtimeAccountId = "test-account", RealtimeAppId = "test-app", RealtimeApiToken = "test-token" };
        var handler = new ReleaseVoiceCleanupTests.Handler { FailKick = true };
        var clock = new ReleaseVoiceCleanupTests.Clock();
        var service = new RealtimeKitService(new ReleaseVoiceCleanupTests.Factory(handler),
            NullLogger<RealtimeKitService>.Instance, fixture.Factory.Services, clock);
        var record = new Valour.Database.RealtimeKitMeeting
        {
            Id = IdManager.Generate(), ChannelId = IdManager.Generate(), MeetingId = Guid.NewGuid().ToString(),
            Status = "ACTIVE", CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow, LastCleanupError = ""
        };
        db.RealtimeKitMeetings.Add(record);
        await db.SaveChangesAsync();
        try
        {
            service.TrackMeetingMapping(record.ChannelId, record.MeetingId);
            await service.CloseTrackedMeetingAsync(record.ChannelId, "test");
            await service.CloseTrackedMeetingAsync(record.ChannelId, "cached retry");
            await db.Entry(record).ReloadAsync();
            Assert.Null(record.ClosedAt);
            Assert.Equal(1, record.CleanupFailureCount);
            Assert.NotEmpty(record.LastCleanupError);
            handler.FailKick = false;
            clock.Advance(TimeSpan.FromMinutes(1));
            await service.CloseTrackedMeetingAsync(record.ChannelId, "recovered");
            await db.Entry(record).ReloadAsync();
            Assert.NotNull(record.ClosedAt);
            Assert.Equal("INACTIVE", record.Status);
            Assert.Equal("", record.LastCleanupError);
            Assert.False(service.GetTrackedChannelMeetingIds().ContainsKey(record.ChannelId));
        }
        finally { await db.RealtimeKitMeetings.Where(x => x.Id == record.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Subscribe_RejectsForeignEndpointTakeoverAndCapsPerUser()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var service = new PushNotificationService(NullLogger<PushNotificationService>.Instance, db, null!, null!, null!);
        var userId = fixture.Client.Me.Id;
        var prefix = $"https://updates.push.services.mozilla.com/wpush/v2/{Guid.NewGuid():N}";
        try
        {
            var owned = ToServerSubscription(ReleasePushDeliveryTests.Subscription($"{prefix}/owned"), userId);
            Assert.True(await service.SubscribeAsync(owned));

            // Same endpoint, different keys, different account: not the same device.
            var takeover = ToServerSubscription(ReleasePushDeliveryTests.Subscription($"{prefix}/owned"), userId + 1);
            Assert.False(await service.SubscribeAsync(takeover));
            var stored = await db.PushNotificationSubscriptions.AsNoTracking().SingleAsync(x => x.Endpoint == owned.Endpoint);
            Assert.Equal(userId, stored.UserId);
            Assert.Equal(owned.Key, stored.Key);

            Assert.False(await service.SubscribeAsync(
                ToServerSubscription(ReleasePushDeliveryTests.Subscription("http://169.254.169.254/latest"), userId)));

            for (var i = 0; i < PushSubscriptionPolicy.MaxSubscriptionsPerUser + 2; i++)
            {
                Assert.True(await service.SubscribeAsync(
                    ToServerSubscription(ReleasePushDeliveryTests.Subscription($"{prefix}/{i}"), userId)));
            }

            Assert.Equal(PushSubscriptionPolicy.MaxSubscriptionsPerUser,
                await db.PushNotificationSubscriptions.CountAsync(x => x.UserId == userId));
            Assert.True(await db.PushNotificationSubscriptions.AnyAsync(
                x => x.Endpoint == $"{prefix}/{PushSubscriptionPolicy.MaxSubscriptionsPerUser + 1}"));
        }
        finally { await db.PushNotificationSubscriptions.Where(x => x.Endpoint.StartsWith(prefix)).ExecuteDeleteAsync(); }
    }

    private static Valour.Server.Models.PushNotificationSubscription ToServerSubscription(
        Valour.Database.PushNotificationSubscription subscription, long userId) => new()
    {
        Endpoint = subscription.Endpoint,
        Key = subscription.Key,
        Auth = subscription.Auth,
        DeviceType = subscription.DeviceType,
        UserId = userId
    };

    [Fact]
    public async Task GoneEndpoint_IsDeletedAndHealthySubscriptionIsKept()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var handler = new ReleasePushDeliveryTests.Handler { Failure = ReleasePushDeliveryTests.Failure.Gone };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var delivery = ReleasePushDeliveryTests.Create(handler, key);
        var service = new PushNotificationService(NullLogger<PushNotificationService>.Instance, db, null!, null!, delivery);
        var prefix = $"https://updates.push.services.mozilla.com/wpush/v2/{Guid.NewGuid():N}";
        var subscriptions = new[]
        {
            ReleasePushDeliveryTests.Subscription($"{prefix}/failure"),
            ReleasePushDeliveryTests.Subscription($"{prefix}/healthy")
        };
        foreach (var subscription in subscriptions)
        {
            subscription.Id = IdManager.Generate();
            subscription.UserId = fixture.Client.Me.Id;
            subscription.ExpiresAt = DateTime.UtcNow.AddDays(7);
        }
        db.PushNotificationSubscriptions.AddRange(subscriptions);
        await db.SaveChangesAsync();
        var ids = subscriptions.Select(x => x.Id).ToArray();
        try
        {
            await service.SendParallelNotificationsAsync(subscriptions, "payload");
            Assert.False(await db.PushNotificationSubscriptions.AnyAsync(x => x.Id == ids[0]));
            Assert.True(await db.PushNotificationSubscriptions.AnyAsync(x => x.Id == ids[1]));
            Assert.Equal(1, handler.Delivered);
        }
        finally { await db.PushNotificationSubscriptions.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(); }
    }
}
