using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;
using DbMessage = Valour.Database.Message;
using Message = Valour.Server.Models.Message;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class SentryMessageQueryTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task MessagePages_PreserveOrderingAndAllRelatedCollections()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var service = scope.ServiceProvider.GetRequiredService<MessageService>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var channelId = IdManager.Generate();
        db.Channels.Add(new Valour.Database.Channel
        {
            Id = channelId, Name = "Sentry query regression", Description = "Isolated message graph", ChannelType = ChannelTypeEnum.DirectChat,
            LastUpdateTime = DateTime.UtcNow, Version = ISharedChannel.CurrentVersion
        });
        var messages = Enumerable.Range(0, 5).Select(i => new DbMessage
        {
            Id = IdManager.Generate(), ChannelId = channelId, AuthorUserId = fixture.Client.Me.Id,
            Content = $"Sentry graph {i}", TimeSent = DateTime.UtcNow,
        }).ToArray();
        foreach (var message in messages)
        {
            if (message != messages[0]) message.ReplyToId = messages[0].Id;
            message.Attachments = Enumerable.Range(0, 2).Select(i => new Valour.Database.MessageAttachment
            {
                Id = IdManager.Generate(), MessageId = message.Id, SortOrder = i,
                Location = $"https://example.com/{i}.png", Type = MessageAttachmentType.Image
            }).ToList();
            message.Mentions = Enumerable.Range(0, 2).Select(i => new Valour.Database.MessageMention
            {
                Id = IdManager.Generate(), MessageId = message.Id, SortOrder = i, TargetId = fixture.Client.Me.Id
            }).ToList();
            message.Reactions = new[] { "👍", "👋" }.Select(emoji => new Valour.Database.MessageReaction
            {
                Id = IdManager.Generate(), MessageId = message.Id, AuthorUserId = fixture.Client.Me.Id, Emoji = emoji, CreatedAt = DateTime.UtcNow
            }).ToList();
        }
        db.Messages.AddRange(messages);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var before = (await service.GetChannelMessagesAsync(null, channelId, count: 2, index: messages[4].Id))!.ToArray();
        Assert.Equal(new[] { messages[2].Id, messages[3].Id }, before.Select(x => x.Id));
        var after = (await service.GetChannelMessagesAfterAsync(null, channelId, messages[1].Id, count: 2))!.ToArray();
        Assert.Equal(before.Select(x => x.Id), after.Select(x => x.Id));
        var search = await service.SearchChannelMessagesAsync(null, channelId, "Sentry graph", count: 2);
        Assert.Equal(new[] { messages[4].Id, messages[3].Id }, search.Select(x => x.Id));
        var cached = (await service.GetChannelMessagesAsync(null, channelId))!.ToArray();
        Assert.Equal(messages.Select(x => x.Id), cached.Select(x => x.Id));
        var single = await service.GetMessageAsync(messages[4].Id);
        Assert.NotNull(single);
        foreach (var message in before.Concat(after).Concat(search).Concat(cached).Append(single))
        {
            AssertCollections(message);
            if (message.Id != messages[0].Id)
            {
                Assert.NotNull(message.ReplyTo);
                Assert.Equal(messages[0].Id, message.ReplyTo.Id);
                Assert.Equal(2, message.ReplyTo.Attachments.Count);
                Assert.Equal(2, message.ReplyTo.Mentions.Count);
                Assert.Equal(2, message.ReplyTo.Reactions.Count);
            }
        }
        var removal = await service.RemoveReactionAsync(fixture.Client.Me.Id, messages[0].Id, "👍");
        Assert.True(removal.Success, removal.Message);
        var refreshed = (await service.GetChannelMessagesAsync(null, channelId))!.ToArray();
        Assert.Single(refreshed[0].Reactions);
        foreach (var reply in refreshed.Skip(1))
            Assert.Equal("👋", Assert.Single(reply.ReplyTo.Reactions).Emoji);

        await transaction.RollbackAsync();
    }

    private static void AssertCollections(Message message)
    {
        Assert.Equal(2, message.Attachments.Count);
        Assert.Equal(2, message.Attachments.Select(x => x.Location).Distinct().Count());
        Assert.Equal(2, message.Mentions.Count);
        Assert.Equal(2, message.Reactions.Count);
        Assert.Equal(2, message.Reactions.Select(x => x.Emoji).Distinct().Count());
    }
}
