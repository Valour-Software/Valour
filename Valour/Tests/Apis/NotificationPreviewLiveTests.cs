using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.E2ee;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// The keys an app keeps so its background code can show message text in
/// push notifications, through the real server and SDK.
/// </summary>
[Collection("ApiCollection")]
public class NotificationPreviewLiveTests
{
    private readonly LoginTestFixture _fixture;

    public NotificationPreviewLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class MemoryNotificationKeyStore : INotificationKeyStore
    {
        public string Value { get; private set; }

        public Task<string> LoadAsync() => Task.FromResult(Value);

        public Task SaveAsync(string keySet)
        {
            Value = keySet;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private static async Task<NotificationKeySet> WaitForKeysAsync(MemoryNotificationKeyStore store,
        Func<NotificationKeySet, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var keys = NotificationKeySet.Parse(store.Value);
            if (keys is not null && ready(keys))
                return keys;
            await Task.Delay(100);
        }

        return NotificationKeySet.Parse(store.Value);
    }

    [Fact]
    public async Task ReadingAChat_KeepsAKeyThatDecryptsItsPushEnvelopes()
    {
        var aliceDetails = await _fixture.RegisterUser();
        var bobDetails = await _fixture.RegisterUser();
        var alice = await EncryptedChat.LoginAsync(_fixture, aliceDetails);
        var bob = await EncryptedChat.LoginAsync(_fixture, bobDetails);
        var store = new MemoryNotificationKeyStore();
        bob.E2eeService.NotificationKeyStore = store;

        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var first = await EncryptedChat.SendAsync(alice, dm, "first");
        Assert.True(first.Success, first.Message);

        // Bob's app verifies the chat's keys when it reads the chat.
        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        await bobDm.GetMessagesAsync(long.MaxValue, 20);
        var keys = await WaitForKeysAsync(store, k => k.Keys.Any(x => x.ChannelId == dm.Id.ToString()));
        Assert.NotNull(keys);
        Assert.Equal(bob.Me.Id.ToString(), keys.UserId);

        // A later message arrives while the app is closed. Its push carries
        // the envelope the server stored, which the kept key decrypts.
        var later = await EncryptedChat.SendAsync(alice, dm, "lunch at **noon**? ||surprise||");
        Assert.True(later.Success, later.Message);
        var stored = await EncryptedChat.WaitForStoredAsync(_fixture, later.Data.Id);
        var envelope = Valour.Server.Services.NotificationService.PreviewEnvelopeOf(stored.EncryptionVersion,
            stored.Envelope);
        Assert.NotNull(envelope);

        var payload = Valour.Server.Services.PushNotificationService.GetPayload(new Valour.Server.Models.NotificationContent
        {
            Title = "Alice DMed you.",
            Message = Valour.Server.Services.NotificationService.EncryptedMessageBody,
            ChannelId = dm.Id,
            Envelope = envelope,
        });
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        var pushed = Convert.FromBase64String(json.RootElement.GetProperty("envelope").GetString()!);

        var preview = NotificationPreviewDecryptor.TryDecrypt(keys, 0, dm.Id, pushed);
        Assert.Equal("lunch at noon? (spoiler)", NotificationPreviewText.Describe(preview));

        // The keys are deleted when Bob logs out.
        await bob.AuthService.LogoutAsync();
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task NewKey_IsKeptByTheDeviceThatMadeIt()
    {
        var aliceDetails = await _fixture.RegisterUser();
        var bobDetails = await _fixture.RegisterUser();
        var alice = await EncryptedChat.LoginAsync(_fixture, aliceDetails);
        var bob = await EncryptedChat.LoginAsync(_fixture, bobDetails);
        var store = new MemoryNotificationKeyStore();
        alice.E2eeService.NotificationKeyStore = store;

        // Alice's first message makes the chat's first key.
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var sent = await EncryptedChat.SendAsync(alice, dm, "hi");
        Assert.True(sent.Success, sent.Message);

        var keys = await WaitForKeysAsync(store, k => k.Keys.Any(x => x.ChannelId == dm.Id.ToString()));
        Assert.NotNull(keys?.Find(0, dm.Id, dm.EncryptionGeneration));
    }

    [Fact]
    public async Task EndingASession_DeletesItsPushSubscriptions()
    {
        var details = await _fixture.RegisterUser();
        var client = await EncryptedChat.LoginAsync(_fixture, details);
        var endpoint = "dXk3bm9fOjE:APA91bH-" + Guid.NewGuid().ToString("N");

        var subscribed = await client.PrimaryNode.PostAsync("api/notifications/subscribe",
            new Valour.Sdk.Models.PushNotificationSubscription
            {
                UserId = client.Me.Id,
                Endpoint = endpoint,
                Key = "",
                Auth = "",
                DeviceType = NotificationDeviceType.AndroidFcmData,
            });
        Assert.True(subscribed.Success, subscribed.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        // The worker stores subscriptions in the background.
        Valour.Database.PushNotificationSubscription stored = null;
        for (var i = 0; i < 60 && stored is null; i++)
        {
            stored = await db.PushNotificationSubscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Endpoint == endpoint);
            if (stored is null)
                await Task.Delay(250);
        }

        Assert.NotNull(stored);
        Assert.Equal(client.AuthService.Token, stored.AuthTokenId);
        Assert.Equal(NotificationDeviceType.AndroidFcmData, stored.DeviceType);

        await client.AuthService.LogoutAsync();

        Assert.False(await db.PushNotificationSubscriptions.AsNoTracking().AnyAsync(x => x.Endpoint == endpoint));
    }
}
