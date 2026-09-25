using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Shared.Models;
using Valour.Shared;
using SdkChannel = Valour.Sdk.Models.Channel;
using SdkMessage = Valour.Sdk.Models.Message;

namespace Valour.Tests;

/// <summary>
/// Sends and reads end-to-end encrypted chat through real SDK clients, the way
/// apps do. Every chat message is encrypted, so server tests that need messages
/// in a channel post them through these clients.
/// </summary>
public static class EncryptedChat
{
    /// <summary>
    /// Logs in as a user with keys kept in <paramref name="store"/>, or in
    /// memory when none is given. Encryption is set up on first login unless
    /// <paramref name="autoSetUp"/> is false. When
    /// <paramref name="requireReady"/> is true, the device must end up ready
    /// to send; otherwise the caller checks its status.
    /// </summary>
    public static async Task<ValourClient> LoginAsync(LoginTestFixture fixture, RegisterUserRequest details,
        IE2eeKeyStore store = null, bool autoSetUp = true, bool requireReady = true)
    {
        // Each client gets its own HttpClient: logging in sets the default
        // authorization header, which must not replace another client's.
        var client = new ValourClient("https://localhost:5001/", httpProvider: fixture.Client.HttpClientProvider);
        var http = fixture.Factory.CreateClient();
        http.BaseAddress = new Uri(client.BaseAddress);
        client.SetHttpClient(http);
        client.E2eeService.KeyStore = store ?? new MemoryE2eeKeyStore();
        client.E2eeService.AutoSetUp = autoSetUp;
        client.E2eeService.AutoRecover = false;
        client.E2eeService.StoreRecoveryCode = false;
        client.E2eeService.DeviceName = "Test device";

        var login = await client.AuthService.LoginAsync(details.Email, details.Password);
        Assert.True(login.Success, login.Message);
        await client.E2eeService.InitializeAsync();
        if (requireReady)
            Assert.Equal(Valour.Sdk.Services.E2eeStatus.Ready, client.E2eeService.Status);
        return client;
    }

    public static async Task<SdkChannel> GetChannelAsync(ValourClient client, long planetId, long channelId)
    {
        var planet = await client.PlanetService.FetchPlanetAsync(planetId, skipCache: true);
        Assert.NotNull(planet);
        await planet.EnsureReadyAsync();
        var channel = await planet.FetchChannelAsync(channelId);
        Assert.NotNull(channel);
        return channel;
    }

    /// <summary>
    /// Sends a message as the client's planet member. The channel's first key
    /// is created if it has none.
    /// </summary>
    public static async Task<TaskResult<SdkMessage>> SendAsync(ValourClient client, long planetId, long channelId,
        string content)
    {
        var channel = await GetChannelAsync(client, planetId, channelId);
        return await SendAsync(client, channel, content, channel.Planet.MyMember?.Id);
    }

    /// <summary>
    /// Sends a message to a channel the client already loaded. Planet
    /// messages need the sender's member ID.
    /// </summary>
    public static Task<TaskResult<SdkMessage>> SendAsync(ValourClient client, SdkChannel channel, string content,
        long? memberId = null, long? replyToId = null) =>
        new SdkMessage(client)
        {
            Content = content,
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            AuthorUserId = client.Me.Id,
            AuthorMemberId = memberId,
            ReplyToId = replyToId,
            Fingerprint = Guid.NewGuid().ToString()
        }.PostAsync();

    /// <summary>
    /// Waits until the server has stored a message. Planet messages are
    /// staged before they are stored, so this polls.
    /// </summary>
    public static async Task<Valour.Database.Message> WaitForStoredAsync(LoginTestFixture fixture, long messageId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        for (var i = 0; i < 60; i++)
        {
            var stored = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId);
            if (stored is not null)
                return stored;
            await Task.Delay(500);
        }

        Assert.Fail("Message was not persisted in time.");
        return null;
    }

    /// <summary>
    /// Has <paramref name="holder"/> publish the channel's first key if needed
    /// and share it with <paramref name="recipient"/>.
    /// </summary>
    public static async Task ShareKeysAsync(ValourClient holder, ValourClient recipient, long planetId, long channelId)
    {
        var holderChannel = await GetChannelAsync(holder, planetId, channelId);
        var enabled = await holder.E2eeService.EnableChannelEncryptionAsync(holderChannel);
        Assert.True(enabled.Success, enabled.Message);

        var recipientChannel = await GetChannelAsync(recipient, planetId, channelId);
        await recipient.E2eeService.RequestKeysAsync(recipientChannel);
        await holder.E2eeService.ServeChannelAsync(holderChannel);
        var ring = await recipient.E2eeService.GetKeyRingAsync(recipientChannel, refresh: true);
        Assert.NotNull(ring?.Latest);
    }

    /// <summary>
    /// Waits for a message the reader can decrypt and that matches the
    /// predicate. Planet messages are staged before they are stored, so this
    /// polls.
    /// </summary>
    public static async Task<SdkMessage> WaitForMessageAsync(ValourClient reader, long planetId, long channelId,
        Func<SdkMessage, bool> predicate, int timeoutMs = 10000)
    {
        var channel = await GetChannelAsync(reader, planetId, channelId);
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            var messages = await channel.GetMessagesAsync(long.MaxValue, 50);
            var match = messages.FirstOrDefault(m =>
                m.DecryptionState == MessageDecryptionState.Decrypted && predicate(m));
            if (match is not null)
                return match;

            await Task.Delay(100);
        }

        return null;
    }
}
