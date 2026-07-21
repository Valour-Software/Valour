using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Services;
using Valour.Shared.Models;
using DbMessage = Valour.Database.Message;
using DbMessageReaction = Valour.Database.MessageReaction;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public sealed class MessageReactionServiceTests : IDisposable
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly MessageService _messageService;
    private readonly UserService _userService;

    public MessageReactionServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _messageService = _scope.ServiceProvider.GetRequiredService<MessageService>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
    }

    [Fact]
    public void ReactionIdentityIndex_IsUnique()
    {
        var entity = _db.Model.FindEntityType(typeof(DbMessageReaction));
        Assert.NotNull(entity);

        var index = entity.GetIndexes().SingleOrDefault(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(DbMessageReaction.MessageId), nameof(DbMessageReaction.AuthorUserId), nameof(DbMessageReaction.Emoji)]));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task AddReactionAsync_SameUserAndEmojiTwice_StoresOnlyOne()
    {
        var userId = _fixture.Client.Me.Id;
        var channel = await _db.Channels.AsNoTracking()
            .Where(x => x.PlanetId == ISharedPlanet.ValourCentralId && x.IsDefault && !x.IsDeleted)
            .FirstAsync();
        var member = await _db.PlanetMembers.AsNoTracking()
            .FirstAsync(x => x.PlanetId == ISharedPlanet.ValourCentralId && x.UserId == userId);

        var dbMessage = new DbMessage
        {
            Id = IdManager.Generate(),
            PlanetId = ISharedPlanet.ValourCentralId,
            ChannelId = channel.Id,
            AuthorUserId = userId,
            AuthorMemberId = member.Id,
            Content = "reaction uniqueness regression",
            TimeSent = DateTime.UtcNow,
        };
        await _db.Messages.AddAsync(dbMessage);
        await _db.SaveChangesAsync();

        try
        {
            var user = await _userService.GetAsync(userId);
            var message = dbMessage.ToModel();

            var first = await _messageService.AddReactionAsync(user, member.ToModel(), message, "👍");
            var second = await _messageService.AddReactionAsync(user, member.ToModel(), message, "👍");

            Assert.True(first.Success, first.Message);
            Assert.False(second.Success);
            Assert.Contains("already exists", second.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(message.Reactions);
            Assert.Equal(1, await _db.MessageReactions.CountAsync(x =>
                x.MessageId == dbMessage.Id && x.AuthorUserId == userId && x.Emoji == "👍"));
        }
        finally
        {
            await _db.MessageReactions.Where(x => x.MessageId == dbMessage.Id).ExecuteDeleteAsync();
            await _db.Messages.Where(x => x.Id == dbMessage.Id).ExecuteDeleteAsync();
        }
    }

    public void Dispose() => _scope.Dispose();
}
