using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DbAutomodAction = Valour.Database.AutomodAction;
using DbAutomodLog = Valour.Database.AutomodLog;
using DbAutomodTrigger = Valour.Database.AutomodTrigger;
using DbMessage = Valour.Database.Message;
using DbMessageAttachment = Valour.Database.MessageAttachment;
using DbMessageMention = Valour.Database.MessageMention;
using DbMessageReaction = Valour.Database.MessageReaction;
using DbModerationAuditLog = Valour.Database.ModerationAuditLog;
using DbPlanetEmoji = Valour.Database.PlanetEmoji;
using DbReport = Valour.Database.Report;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class UserServiceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly UserService _userService;
    private readonly RegisterService _registerService;
    private readonly ValourDb _db;

    private readonly List<User> _createdUsers = new();

    public UserServiceTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _factory = fixture.Factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _registerService = _scope.ServiceProvider.GetRequiredService<RegisterService>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var user in _createdUsers)
        {
            try
            {
                await _userService.HardDelete(user);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsUser()
    {
        var me = _client.Me;
        var result = await _userService.GetByNameAndTagAsync($"{me.Name}#{me.Tag}");
        Assert.NotNull(result);
        Assert.Equal(me.Id, result.Id);
    }

    [Fact]
    public async Task IsTagTaken_ReturnsTrueForExisting()
    {
        var me = _client.Me;
        var taken = await _userService.IsTagTaken(me.Name, me.Tag);
        Assert.True(taken);
    }

    [Fact]
    public async Task GetUniqueTag_ReturnsUnusedTag()
    {
        var me = _client.Me;
        var tag = await _userService.GetUniqueTag(me.Name);
        Assert.NotEqual(me.Tag, tag);
        Assert.False(await _userService.IsTagTaken(me.Name, tag));
    }

    [Fact]
    public void GetYearsOld_CalculatesCorrectly()
    {
        var birthday = DateTime.Today.AddYears(-20).AddDays(-1);
        var age = _userService.GetYearsOld(birthday);
        Assert.Equal(20, age);
    }
    
    private string RandomName()
    {
        return $"test-{Guid.NewGuid():N}".Substring(0, 10);
    }

    private async Task<User> RegisterDisposableUserAsync()
    {
        var randomName = RandomName();

        var req = new RegisterUserRequest
        {
            Username = $"temp-{randomName}",
            Email = $"t{randomName}@test.com",
            Password = "TempPass1!",
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test"
        };

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");

        var reg = await _registerService.RegisterUserAsync(req, ctx, skipEmail: true);
        Assert.True(reg.Success);

        var dbUser = await _db.Users.FirstOrDefaultAsync(x => x.Name == req.Username);
        Assert.NotNull(dbUser);
        var model = dbUser.ToModel();
        _createdUsers.Add(model);

        return model;
    }

    [Fact]
    public async Task HardDelete_RemovesUser()
    {
        var model = await RegisterDisposableUserAsync();

        var del = await _userService.HardDelete(model);
        Assert.True(del.Success, del.Message);
        _createdUsers.Remove(model);

        var check = await _db.Users.FindAsync(model.Id);
        Assert.Null(check);
    }

    [Fact]
    public async Task HardDelete_RemovesReportsAndDirectMessageChannels()
    {
        var model = await RegisterDisposableUserAsync();

        long dmChannelId;
        await using (var arrangeScope = _factory.Services.CreateAsyncScope())
        {
            var arrangeDb = arrangeScope.ServiceProvider.GetRequiredService<ValourDb>();

            var persistedChannelId = await arrangeDb.Channels
                .IgnoreQueryFilters()
                .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat &&
                            x.Members.Any(m => m.UserId == model.Id) &&
                            x.Members.Any(m => m.UserId == ISharedUser.VictorUserId))
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
            Assert.NotNull(persistedChannelId);

            dmChannelId = persistedChannelId.Value;

            arrangeDb.Reports.Add(new DbReport
            {
                Id = Guid.NewGuid().ToString("N"),
                TimeCreated = DateTime.UtcNow,
                ReportingUserId = model.Id,
                ReasonCode = ReportReasonCode.IsSpam,
                LongReason = "regression",
                Reviewed = false,
                Resolution = ReportResolution.None,
                StaffNotes = string.Empty
            });

            await arrangeDb.SaveChangesAsync();
        }

        var del = await _userService.HardDelete(model);
        Assert.True(del.Success, del.Message);
        _createdUsers.Remove(model);

        _db.ChangeTracker.Clear();

        Assert.Null(await _db.Users.FindAsync(model.Id));
        Assert.Null(await _db.Channels.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == dmChannelId));
        Assert.Empty(await _db.Reports.IgnoreQueryFilters().Where(x => x.ReportingUserId == model.Id).ToListAsync());
        Assert.Empty(await _db.ChannelMembers.IgnoreQueryFilters().Where(x => x.ChannelId == dmChannelId).ToListAsync());
    }

    [Fact]
    public async Task HardDelete_CleansRelatedMessageMemberAndAuditData()
    {
        var model = await RegisterDisposableUserAsync();

        var member = await _db.PlanetMembers
            .IgnoreQueryFilters()
            .FirstAsync(x => x.UserId == model.Id && x.PlanetId == ISharedPlanet.ValourCentralId);

        var otherMember = await _db.PlanetMembers
            .IgnoreQueryFilters()
            .FirstAsync(x => x.UserId == _client.Me.Id && x.PlanetId == ISharedPlanet.ValourCentralId);

        var channelId = await _db.Channels
            .Where(x => x.PlanetId == ISharedPlanet.ValourCentralId &&
                        x.IsDefault &&
                        x.ChannelType == ChannelTypeEnum.PlanetChat)
            .Select(x => x.Id)
            .FirstAsync();

        var messageId = IdManager.Generate();
        var replyId = IdManager.Generate();
        var attachmentId = IdManager.Generate();
        var mentionId = IdManager.Generate();
        var messageReactionId = IdManager.Generate();
        var userReactionId = IdManager.Generate();
        var reportId = Guid.NewGuid().ToString("N");
        var auditLogId = IdManager.Generate();
        var automodLogId = Guid.NewGuid();
        var automodActionId = Guid.NewGuid();
        var automodTriggerId = Guid.NewGuid();
        var memberAutomodActionId = Guid.NewGuid();
        var memberAutomodTriggerId = Guid.NewGuid();
        var emojiId = IdManager.Generate();
        var now = DateTime.UtcNow;

        _db.Messages.Add(new DbMessage
        {
            Id = messageId,
            PlanetId = ISharedPlanet.ValourCentralId,
            ChannelId = channelId,
            AuthorUserId = model.Id,
            AuthorMemberId = member.Id,
            Content = "hard delete regression",
            TimeSent = now
        });
        await _db.SaveChangesAsync();

        _db.Messages.Add(new DbMessage
        {
            Id = replyId,
            PlanetId = ISharedPlanet.ValourCentralId,
            ChannelId = channelId,
            AuthorUserId = _client.Me.Id,
            AuthorMemberId = otherMember.Id,
            ReplyToId = messageId,
            Content = "reply should survive",
            TimeSent = now
        });

        _db.MessageAttachments.Add(new DbMessageAttachment
        {
            Id = attachmentId,
            MessageId = messageId,
            SortOrder = 0,
            Type = MessageAttachmentType.File,
            Location = "about:blank",
            MimeType = "text/plain",
            FileName = "regression.txt",
            Width = 0,
            Height = 0,
            Inline = false,
            Missing = false
        });

        _db.MessageMentions.Add(new DbMessageMention
        {
            Id = mentionId,
            MessageId = messageId,
            SortOrder = 0,
            Type = MentionType.User,
            TargetId = _client.Me.Id
        });

        _db.MessageReactions.AddRange(
            new DbMessageReaction
            {
                Id = messageReactionId,
                MessageId = messageId,
                AuthorUserId = _client.Me.Id,
                AuthorMemberId = otherMember.Id,
                Emoji = "thumbsup",
                CreatedAt = now
            },
            new DbMessageReaction
            {
                Id = userReactionId,
                MessageId = replyId,
                AuthorUserId = model.Id,
                AuthorMemberId = member.Id,
                Emoji = "ok",
                CreatedAt = now
            });

        _db.Reports.Add(new DbReport
        {
            Id = reportId,
            TimeCreated = now,
            ReportingUserId = _client.Me.Id,
            ReportedUserId = model.Id,
            MessageId = messageId,
            PlanetId = ISharedPlanet.ValourCentralId,
            ReasonCode = ReportReasonCode.IsSpam,
            LongReason = "regression",
            Reviewed = false,
            Resolution = ReportResolution.None,
            StaffNotes = string.Empty
        });

        _db.ModerationAuditLogs.Add(new DbModerationAuditLog
        {
            Id = auditLogId,
            PlanetId = ISharedPlanet.ValourCentralId,
            ActorUserId = model.Id,
            TargetUserId = model.Id,
            TargetMemberId = member.Id,
            MessageId = messageId,
            Source = ModerationActionSource.Manual,
            ActionType = ModerationActionType.DeleteMessage,
            Details = "regression",
            TimeCreated = now
        });

        _db.AutomodTriggers.Add(new DbAutomodTrigger
        {
            Id = automodTriggerId,
            PlanetId = ISharedPlanet.ValourCentralId,
            MemberAddedBy = otherMember.Id,
            Type = AutomodTriggerType.Blacklist,
            Name = "Hard delete regression",
            TriggerWords = "hard-delete-regression"
        });

        _db.AutomodTriggers.Add(new DbAutomodTrigger
        {
            Id = memberAutomodTriggerId,
            PlanetId = ISharedPlanet.ValourCentralId,
            MemberAddedBy = member.Id,
            Type = AutomodTriggerType.Blacklist,
            Name = "Hard delete member regression",
            TriggerWords = "hard-delete-member-regression"
        });
        await _db.SaveChangesAsync();

        _db.AutomodLogs.Add(new DbAutomodLog
        {
            Id = automodLogId,
            PlanetId = ISharedPlanet.ValourCentralId,
            TriggerId = automodTriggerId,
            MemberId = member.Id,
            MessageId = messageId,
            TimeTriggered = now
        });

        _db.AutomodActions.Add(new DbAutomodAction
        {
            Id = automodActionId,
            TriggerId = automodTriggerId,
            PlanetId = ISharedPlanet.ValourCentralId,
            MemberAddedBy = otherMember.Id,
            TargetMemberId = otherMember.Id,
            ActionType = AutomodActionType.DeleteMessage,
            MessageId = messageId,
            Message = "regression"
        });

        _db.AutomodActions.Add(new DbAutomodAction
        {
            Id = memberAutomodActionId,
            TriggerId = memberAutomodTriggerId,
            PlanetId = ISharedPlanet.ValourCentralId,
            MemberAddedBy = member.Id,
            TargetMemberId = member.Id,
            ActionType = AutomodActionType.DeleteMessage,
            MessageId = messageId,
            Message = "member regression"
        });

        _db.PlanetEmojis.Add(new DbPlanetEmoji
        {
            Id = emojiId,
            PlanetId = ISharedPlanet.ValourCentralId,
            CreatorUserId = model.Id,
            Name = RandomEmojiName(),
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        var del = await _userService.HardDelete(model);
        Assert.True(del.Success, del.Message);
        _createdUsers.Remove(model);

        _db.ChangeTracker.Clear();

        Assert.Null(await _db.Users.FindAsync(model.Id));
        Assert.Null(await _db.Messages.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == messageId));
        Assert.Null(await _db.MessageAttachments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == attachmentId));
        Assert.Null(await _db.MessageMentions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == mentionId));
        Assert.Null(await _db.MessageReactions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == messageReactionId));
        Assert.Null(await _db.MessageReactions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userReactionId));

        var reply = await _db.Messages.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == replyId);
        Assert.NotNull(reply);
        Assert.Null(reply.ReplyToId);

        var report = await _db.Reports.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == reportId);
        Assert.NotNull(report);
        Assert.Null(report.ReportedUserId);
        Assert.Null(report.MessageId);

        var auditLog = await _db.ModerationAuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == auditLogId);
        Assert.NotNull(auditLog);
        Assert.Null(auditLog.ActorUserId);
        Assert.Null(auditLog.TargetUserId);
        Assert.Null(auditLog.TargetMemberId);
        Assert.Null(auditLog.MessageId);

        Assert.Null(await _db.AutomodLogs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == automodLogId));

        var automodAction = await _db.AutomodActions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == automodActionId);
        Assert.NotNull(automodAction);
        Assert.Null(automodAction.MessageId);
        Assert.Null(await _db.AutomodActions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == memberAutomodActionId));
        Assert.Null(await _db.AutomodTriggers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == memberAutomodTriggerId));

        Assert.Null(await _db.PlanetEmojis.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == emojiId));

        _db.AutomodActions.Remove(automodAction);
        var automodTrigger = await _db.AutomodTriggers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == automodTriggerId);
        if (automodTrigger is not null)
            _db.AutomodTriggers.Remove(automodTrigger);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetUserComplianceData_FailsForUnderage()
    {
        var me = _client.Me;
        var birth = DateTime.Today.AddYears(-10);
        var result = await _userService.SetUserComplianceData(me.Id, birth);
        Assert.False(result.Success);
    }

    private static string RandomEmojiName() =>
        $"e_{Guid.NewGuid():N}".Substring(0, 16);
}
