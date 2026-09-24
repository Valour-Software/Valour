using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// Hiding replies to a blocked user's messages applies to the viewer who blocked them. The
/// latest messages of a channel come from the node's shared chat cache, so hiding a reply
/// preview must not change the cached message that every other viewer receives.
/// </summary>
[Collection("ApiCollection")]
public class BlockedReplyPreviewLiveTests
{
    private readonly LoginTestFixture _fixture;

    public BlockedReplyPreviewLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BlockedReplyPreview_IsHiddenForViewer_WithoutChangingSharedCache()
    {
        var viewer = _fixture.Client;

        var create = await new Valour.Sdk.Models.Planet(viewer)
        {
            Name = $"Block Reply {Guid.NewGuid().ToString()[..8]}",
            Description = "Blocked reply preview test planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        var planet = await viewer.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        var channel = await planet.FetchPrimaryChatChannelAsync();
        await planet.EnsureReadyAsync();

        // A second user joins the planet and writes the message that gets replied to
        var blockedDetails = await _fixture.RegisterUser();
        var blockedClient = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(_fixture.Factory));
        blockedClient.SetHttpClient(_fixture.Factory.CreateClient());
        var login = await blockedClient.AuthService.LoginAsync(blockedDetails.Email, blockedDetails.Password);
        Assert.True(login.Success, login.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var memberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var blockService = scope.ServiceProvider.GetRequiredService<UserBlockService>();
        var chatCache = scope.ServiceProvider.GetRequiredService<ChatCacheService>();

        var join = await memberService.AddMemberAsync(planet.Id, blockedClient.Me.Id);
        Assert.True(join.Success, join.Message);

        var blockedPlanet = await blockedClient.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await blockedPlanet.EnsureReadyAsync();

        var original = await SendAsync(blockedClient, planet.Id, channel.Id, join.Data!.Id, "original message");
        var reply = await SendAsync(viewer, planet.Id, channel.Id, planet.MyMember?.Id, "reply", original.Id);

        await WaitForPersistedAsync(original.Id);
        await WaitForPersistedAsync(reply.Id);

        // Before blocking, the latest messages (served from the chat cache) include the preview
        var before = await FetchLatestAsync(viewer, planet.Id, channel.Id);
        Assert.True(HasReplyPreview(before, reply.Id), "The reply should include its preview before blocking.");

        var block = await blockService.BlockUserAsync(viewer.Me.Id, blockedClient.Me.Id, BlockType.OneWay);
        Assert.True(block.Success, block.Message);

        try
        {
            var after = await FetchLatestAsync(viewer, planet.Id, channel.Id);
            Assert.False(HasReplyPreview(after, reply.Id), "The blocking viewer should not see the preview.");

            var cached = await chatCache.GetLastMessagesAsync(channel.Id);
            var cachedReply = Assert.Single(cached, x => x.Id == reply.Id);
            Assert.NotNull(cachedReply.ReplyTo);
            Assert.Equal(original.Id, cachedReply.ReplyToId);
        }
        finally
        {
            await blockService.UnblockUserAsync(viewer.Me.Id, blockedClient.Me.Id);
        }
    }

    private static async Task<Valour.Sdk.Models.Message> SendAsync(
        ValourClient client, long planetId, long channelId, long? memberId, string content, long? replyToId = null)
    {
        var message = new Valour.Sdk.Models.Message(client)
        {
            Content = content,
            ChannelId = channelId,
            PlanetId = planetId,
            AuthorUserId = client.Me.Id,
            AuthorMemberId = memberId,
            ReplyToId = replyToId,
            Fingerprint = Guid.NewGuid().ToString(),
        };

        var result = await client.MessageService.SendMessage(message);
        Assert.True(result.Success, result.Message);
        return result.Data;
    }

    private async Task WaitForPersistedAsync(long messageId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        for (var i = 0; i < 60; i++)
        {
            if (await db.Messages.AnyAsync(x => x.Id == messageId))
                return;
            await Task.Delay(500);
        }

        Assert.Fail("Message was not persisted in time.");
    }

    // Fifty messages from the newest end is the request the chat cache serves
    private static async Task<JsonElement> FetchLatestAsync(ValourClient client, long planetId, long channelId)
    {
        var response = await client.Http.GetAsync($"api/planets/{planetId}/channels/{channelId}/messages?count=50");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static bool HasReplyPreview(JsonElement messages, long messageId)
    {
        foreach (var message in messages.EnumerateArray())
        {
            if (GetProperty(message, "id")?.GetInt64() != messageId)
                continue;

            var replyTo = GetProperty(message, "replyTo");
            return replyTo is { ValueKind: JsonValueKind.Object };
        }

        Assert.Fail($"Message {messageId} was not returned.");
        return false;
    }

    private static JsonElement? GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}
