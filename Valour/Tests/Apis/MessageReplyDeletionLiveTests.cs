using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;

namespace Valour.Tests.Apis;

/// <summary>
/// Deleting a message that other messages reply to must succeed; the replies
/// survive with their reply reference cleared (FK ON DELETE SET NULL, plus
/// staged-message cleanup for not-yet-persisted replies).
/// </summary>
[Collection("ApiCollection")]
public class MessageReplyDeletionLiveTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;

    private Valour.Sdk.Models.Planet _planet = null!;
    private Valour.Sdk.Models.Channel _channel = null!;

    public MessageReplyDeletionLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var create = await new Valour.Sdk.Models.Planet(_fixture.Client)
        {
            Name = $"Reply Del {Guid.NewGuid().ToString()[..8]}",
            Description = "Reply deletion test planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        _planet = await _fixture.Client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        _channel = await _planet.FetchPrimaryChatChannelAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<Valour.Sdk.Models.Message> SendAsync(string content, long? replyToId = null)
    {
        await _planet.EnsureReadyAsync();

        var message = new Valour.Sdk.Models.Message(_fixture.Client)
        {
            Content = content,
            ChannelId = _channel.Id,
            PlanetId = _planet.Id,
            AuthorUserId = _fixture.Client.Me.Id,
            AuthorMemberId = _planet.MyMember?.Id,
            ReplyToId = replyToId,
            Fingerprint = Guid.NewGuid().ToString(),
        };

        var result = await _fixture.Client.MessageService.SendMessage(message);
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

    [Fact]
    public async Task DeletingRepliedToMessage_Succeeds_AndClearsReferences()
    {
        var original = await SendAsync("original message");
        var reply = await SendAsync("a reply", replyToId: original.Id);

        // Both persisted: the reply's FK reference exists in the database
        await WaitForPersistedAsync(original.Id);
        await WaitForPersistedAsync(reply.Id);

        var delete = await _fixture.Client.Http.DeleteAsync($"api/messages/{original.Id}");
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        Assert.False(await db.Messages.AnyAsync(x => x.Id == original.Id));

        var survivingReply = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reply.Id);
        Assert.NotNull(survivingReply);
        Assert.Null(survivingReply.ReplyToId);
    }

    [Fact]
    public async Task DeletingRepliedToMessage_WithStagedReply_Succeeds()
    {
        var original = await SendAsync("original for staged reply");
        await WaitForPersistedAsync(original.Id);

        // The reply is likely still staged (not yet flushed) right after send
        var reply = await SendAsync("staged reply", replyToId: original.Id);

        var delete = await _fixture.Client.Http.DeleteAsync($"api/messages/{original.Id}");
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        // The reply must still flush to the database without an FK violation
        await WaitForPersistedAsync(reply.Id);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        var survivingReply = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reply.Id);
        Assert.NotNull(survivingReply);
        Assert.Null(survivingReply.ReplyToId);
    }
}
