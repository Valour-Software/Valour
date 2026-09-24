using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Server.Workers;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class AutomodServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _memberService;
    private readonly MessageService _messageService;
    private readonly AutomodService _automodService;

    private Planet _planet = null!;
    private User _owner = null!;
    private PlanetMember _ownerMember = null!;
    private Channel _defaultChannel = null!;
    private readonly List<User> _createdUsers = new();

    public AutomodServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        var factory = fixture.Factory;

        _scope = factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _messageService = _scope.ServiceProvider.GetRequiredService<MessageService>();
        _automodService = _scope.ServiceProvider.GetRequiredService<AutomodService>();
    }

    public async ValueTask InitializeAsync()
    {
        _owner = await _userService.GetAsync(_fixture.Client.Me.Id);

        var createResult = await _planetService.CreateAsync(new Planet
        {
            Name = "Automod Test Planet",
            Description = "Planet for automod service tests",
            OwnerId = _owner.Id
        }, _owner);

        Assert.True(createResult.Success, createResult.Message);
        Assert.NotNull(createResult.Data);
        _planet = createResult.Data!;

        _ownerMember = await _memberService.GetByUserAsync(_owner.Id, _planet.Id);
        Assert.NotNull(_ownerMember);

        _defaultChannel = await _db.Channels
            .AsNoTracking()
            .Where(c => c.PlanetId == _planet.Id && c.IsDefault)
            .Select(c => c.ToModel())
            .FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var user in _createdUsers)
        {
            await _userService.HardDelete(user);
        }

        if (_planet is not null)
        {
            await _planetService.DeleteAsync(_planet.Id);
        }

        _scope.Dispose();
    }

    [Fact]
    public async Task Sentry_QueryPages_AreOrderedAndClampNegativeBounds()
    {
        var ids = new[] { Guid.Parse("30000000-0000-0000-0000-000000000000"), Guid.Parse("10000000-0000-0000-0000-000000000000"), Guid.Parse("20000000-0000-0000-0000-000000000000") };
        // Shuffle the leading bytes but keep IDs unique between repeated test runs.
        var suffix = Guid.NewGuid().ToString()[8..];
        ids = ids.Select(x => Guid.Parse(x.ToString()[..8] + suffix)).ToArray();
        foreach (var id in ids)
        {
            _db.AutomodTriggers.Add(new Valour.Database.AutomodTrigger { Id = id, PlanetId = _planet.Id, MemberAddedBy = _ownerMember.Id, Name = "Paging", Type = AutomodTriggerType.Blacklist });
            _db.AutomodActions.Add(new Valour.Database.AutomodAction { Id = id, TriggerId = ids[0], PlanetId = _planet.Id, MemberAddedBy = _ownerMember.Id, Message = "Paging", ActionType = AutomodActionType.Respond });
        }
        await _db.SaveChangesAsync();
        var expected = ids.Order().ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            var request = new QueryRequest { Skip = i, Take = 1 };
            var triggers = await _automodService.QueryPlanetTriggersAsync(_planet.Id, request);
            var actions = await _automodService.QueryTriggerActionsAsync(_planet.Id, ids[0], request);
            Assert.Equal(3, triggers.TotalCount);
            Assert.Equal(expected[i], Assert.Single(triggers.Items).Id);
            Assert.Equal(3, actions.TotalCount);
            Assert.Equal(expected[i], Assert.Single(actions.Items).Id);
        }
        Assert.Equal(expected[0], Assert.Single((await _automodService.QueryPlanetTriggersAsync(_planet.Id, new QueryRequest { Skip = -1, Take = 1 })).Items).Id);
        Assert.Empty((await _automodService.QueryTriggerActionsAsync(_planet.Id, ids[0], new QueryRequest { Take = -1 })).Items);
    }

    [Fact]
    public async Task CreateTriggerWithActions_AddRoleAction_PersistsRoleId()
    {
        // Server-side contract behind #1477: the selected role of an
        // AddRole/RemoveRole action must round-trip through create + reopen
        var roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        var roleResult = await roleService.CreateAsync(new PlanetRole
        {
            Name = "Automod role",
            PlanetId = _planet.Id,
            Permissions = 0,
            ChatPermissions = 0,
            CategoryPermissions = 0,
            VoicePermissions = 0
        });
        Assert.True(roleResult.Success, roleResult.Message);
        var role = roleResult.Data!;

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Role trigger",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "role-trigger-word"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.AddRole,
            RoleId = role.Id,
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        // The reopen path in the client loads actions via the query endpoint
        var page = await _automodService.QueryTriggerActionsAsync(
            _planet.Id, createResult.Data!.Id, new QueryRequest { Take = 10 });
        var fetched = Assert.Single(page.Items);
        Assert.Equal(role.Id, fetched.RoleId);
    }

    [Fact]
    public async Task ScanMessage_BlacklistRespond_PostsVictorResponse()
    {
        var regularMember = await RegisterAndJoinPlanetAsync();

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Blacklist respond",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "blacklist-trigger-word"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.Respond,
            Message = "Automod response text",
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = regularMember.UserId,
            AuthorMemberId = regularMember.Id,
            Content = "hello blacklist-trigger-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);

        var response = await WaitForMessageAsync(_defaultChannel.Id, m =>
            m.AuthorUserId == ISharedUser.VictorUserId &&
            m.Content.Contains("Automod response text", StringComparison.Ordinal) &&
            m.Content.Contains($"«@m-{regularMember.Id}»", StringComparison.Ordinal));

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ScanMessage_BlacklistBlockMessage_PreventsPosting()
    {
        var regularMember = await RegisterAndJoinPlanetAsync();

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Blacklist block",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "blocked-by-automod"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.BlockMessage,
            Message = string.Empty,
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var blockedMessage = new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = regularMember.UserId,
            AuthorMemberId = regularMember.Id,
            Content = "this should be blocked-by-automod",
            Fingerprint = Guid.NewGuid().ToString()
        };

        var postResult = await _messageService.PostMessageAsync(blockedMessage);

        Assert.False(postResult.Success);
        Assert.Contains("automod", postResult.Message, StringComparison.OrdinalIgnoreCase);

        var persisted = await _db.Messages.AnyAsync(m => m.Id == blockedMessage.Id);
        Assert.False(persisted);
    }

    [Fact]
    public async Task ScanMessage_BlockedMessage_DoesNotSendMentionNotifications()
    {
        var author = await RegisterAndJoinPlanetAsync();
        var target = await RegisterAndJoinPlanetAsync();
        await CreateBlockTriggerAsync("mention-blocked-word");

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = author.UserId,
            AuthorMemberId = author.Id,
            Content = $"«@m-{target.Id}» mention-blocked-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.False(postResult.Success);
        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == target.UserId && x.ChannelId == _defaultChannel.Id));
    }

    [Fact]
    public async Task EditMessage_IntoBlockedContent_IsRejected()
    {
        var member = await RegisterAndJoinPlanetAsync();
        await CreateBlockTriggerAsync("edit-blocked-word");

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = member.UserId,
            AuthorMemberId = member.Id,
            Content = "clean original content",
            Fingerprint = Guid.NewGuid().ToString()
        });
        Assert.True(postResult.Success, postResult.Message);
        var messageId = postResult.Data!.Id;

        var editResult = await _messageService.EditMessageAsync(new Message
        {
            Id = messageId,
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            Content = "now with edit-blocked-word"
        });

        Assert.False(editResult.Success);
        Assert.Contains("automod", editResult.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await _messageService.GetMessageAsync(messageId);
        Assert.NotNull(stored);
        Assert.Equal("clean original content", stored!.Content);
    }

    [Fact]
    public async Task ScanMessage_BlacklistBan_BansTargetMember()
    {
        var regularMember = await RegisterAndJoinPlanetAsync();

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Blacklist ban",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "ban-trigger-word"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.Ban,
            Message = "Banned by automod test",
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = regularMember.UserId,
            AuthorMemberId = regularMember.Id,
            Content = "triggering ban-trigger-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);

        var banExists = await _db.PlanetBans.AnyAsync(b => b.PlanetId == _planet.Id && b.TargetId == regularMember.UserId);
        Assert.True(banExists);

        var memberDeleted = await _db.PlanetMembers
            .IgnoreQueryFilters()
            .Where(m => m.Id == regularMember.Id)
            .Select(m => m.IsDeleted)
            .FirstAsync();
        Assert.True(memberDeleted);
    }

    [Fact]
    public async Task DeleteMessage_ImmediatelyAfterPost_Succeeds()
    {
        var regularMember = await RegisterAndJoinPlanetAsync();

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = regularMember.UserId,
            AuthorMemberId = regularMember.Id,
            Content = "delete-me-immediately",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);
        Assert.NotNull(postResult.Data);

        var messageId = postResult.Data!.Id;
        var deleteResult = await _messageService.DeleteMessageAsync(messageId);
        Assert.True(deleteResult.Success, deleteResult.Message);

        Assert.Null(PlanetMessageWorker.GetStagedMessage(messageId));
        Assert.False(await _db.Messages.AnyAsync(m => m.Id == messageId));
    }

    [Fact]
    public async Task HandleMemberJoin_JoinRespond_PostsVictorResponse()
    {
        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Join respond",
            Type = AutomodTriggerType.Join
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.Respond,
            Message = "Welcome to the planet",
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var newUser = await RegisterNewUserAsync();
        var joinResult = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(joinResult.Success, joinResult.Message);
        Assert.NotNull(joinResult.Data);

        var joinedMember = joinResult.Data!;

        Assert.True(await _db.AutomodLogs.AnyAsync(x =>
            x.TriggerId == trigger.Id && x.MemberId == joinedMember.Id));

        var response = await WaitForMessageAsync(_defaultChannel.Id, m =>
            m.AuthorUserId == ISharedUser.VictorUserId &&
            m.Content.Contains("Welcome to the planet", StringComparison.Ordinal) &&
            m.Content.Contains($"«@m-{joinedMember.Id}»", StringComparison.Ordinal));

        Assert.NotNull(response);
    }

    [Fact]
    public async Task DeleteTrigger_WhenLogsExist_DeletesTriggerAndLogs()
    {
        var regularMember = await RegisterAndJoinPlanetAsync();

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Delete me",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "delete-trigger-word"
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, []);
        Assert.True(createResult.Success, createResult.Message);

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = regularMember.UserId,
            AuthorMemberId = regularMember.Id,
            Content = "contains delete-trigger-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);

        var logExists = await _db.AutomodLogs.AnyAsync(x => x.TriggerId == trigger.Id);
        Assert.True(logExists);

        var deleteResult = await _automodService.DeleteTriggerAsync(trigger);
        Assert.True(deleteResult.Success, deleteResult.Message);

        var dbTrigger = await _db.AutomodTriggers.FindAsync(trigger.Id);
        Assert.Null(dbTrigger);
        Assert.False(await _db.AutomodLogs.AnyAsync(x => x.TriggerId == trigger.Id));
        Assert.False(await _db.AutomodActions.AnyAsync(x => x.TriggerId == trigger.Id));
    }

    [Fact]
    public async Task ScanMessage_BanActionFromCreatorWithoutAuthority_IsSkipped()
    {
        // A regular member has neither the Ban permission nor authority over other members
        var creator = await RegisterAndJoinPlanetAsync();
        var target = await RegisterAndJoinPlanetAsync();

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = creator.Id,
            Name = "Unauthorized ban",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "unauthorized-ban-word"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = creator.Id,
            ActionType = AutomodActionType.Ban,
            Message = "Should not ban",
            Strikes = 1
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = target.UserId,
            AuthorMemberId = target.Id,
            Content = "triggering unauthorized-ban-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);
        Assert.False(await _db.PlanetBans.AnyAsync(b => b.PlanetId == _planet.Id && b.TargetId == target.UserId));
        Assert.False(await _db.PlanetMembers.IgnoreQueryFilters()
            .Where(m => m.Id == target.Id)
            .Select(m => m.IsDeleted)
            .FirstAsync());
    }

    [Fact]
    public async Task ScanMessage_AddRoleActionTargetingAdmin_IsSkipped()
    {
        var roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        var adminRole = (await roleService.CreateAsync(new PlanetRole
        {
            Name = "Automod admin",
            PlanetId = _planet.Id,
            IsAdmin = true
        })).Data!;
        var plainRole = (await roleService.CreateAsync(new PlanetRole
        {
            Name = "Automod plain",
            PlanetId = _planet.Id
        })).Data!;

        var admin = await RegisterAndJoinPlanetAsync();
        var addAdmin = await _memberService.AddRoleAsync(_planet.Id, admin.Id, adminRole.Id);
        Assert.True(addAdmin.Success, addAdmin.Message);

        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Role on admin",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "admin-role-word",
            RunForEveryone = true
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.AddRole,
            RoleId = plainRole.Id,
            Strikes = 1
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var postResult = await _messageService.PostMessageAsync(new Message
        {
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = admin.UserId,
            AuthorMemberId = admin.Id,
            Content = "admin-role-word",
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.True(postResult.Success, postResult.Message);
        var adminAfter = await _memberService.GetAsync(admin.Id);
        Assert.False(adminAfter!.RoleMembership.HasRole(plainRole.FlagBitIndex));
    }

    [Fact]
    public async Task ValidateAction_RejectsRolesOutsideAuthority()
    {
        var roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        var adminRole = (await roleService.CreateAsync(new PlanetRole
        {
            Name = "Validate admin",
            PlanetId = _planet.Id,
            IsAdmin = true
        })).Data!;
        var plainRole = (await roleService.CreateAsync(new PlanetRole
        {
            Name = "Validate plain",
            PlanetId = _planet.Id
        })).Data!;

        AutomodAction RoleAction(long roleId) => new()
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.AddRole,
            RoleId = roleId
        };

        Assert.False((await _automodService.ValidateActionAsync(RoleAction(adminRole.Id), _ownerMember)).Success);
        Assert.False((await _automodService.ValidateActionAsync(RoleAction(IdManager.Generate()), _ownerMember)).Success);
        Assert.True((await _automodService.ValidateActionAsync(RoleAction(plainRole.Id), _ownerMember)).Success);

        // A member without ManageRoles cannot configure role actions at all
        var regular = await RegisterAndJoinPlanetAsync();
        Assert.False((await _automodService.ValidateActionAsync(RoleAction(plainRole.Id), regular)).Success);
    }

    [Fact]
    public async Task CreateAction_RejectsTriggerFromAnotherPlanet()
    {
        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Foreign trigger",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "foreign-trigger-word"
        };
        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, []);
        Assert.True(createResult.Success, createResult.Message);

        var result = await _automodService.CreateActionAsync(new AutomodAction
        {
            PlanetId = ISharedPlanet.ValourCentralId,
            TriggerId = trigger.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.BlockMessage
        });

        Assert.False(result.Success);
        Assert.False(await _db.AutomodActions.AnyAsync(x => x.TriggerId == trigger.Id));
    }

    [Fact]
    public async Task ScanMessage_WebhookMessage_IsBlockedByBlacklist()
    {
        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Webhook block",
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = "webhook-blocked-word"
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.BlockMessage,
            Strikes = 1
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);

        var message = new Message
        {
            Id = IdManager.Generate(),
            PlanetId = _planet.Id,
            ChannelId = _defaultChannel.Id,
            AuthorUserId = ISharedUser.VictorUserId,
            WebhookId = IdManager.Generate(),
            Content = "a webhook-blocked-word message",
            TimeSent = DateTime.UtcNow
        };

        var blocked = await _automodService.ScanMessageAsync(message, null!);
        Assert.False(blocked.AllowMessage);

        message.Content = "a harmless message";
        var allowed = await _automodService.ScanMessageAsync(message, null!);
        Assert.True(allowed.AllowMessage);
    }

    private async Task CreateBlockTriggerAsync(string word)
    {
        var trigger = new AutomodTrigger
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            Name = "Blacklist block " + word,
            Type = AutomodTriggerType.Blacklist,
            TriggerWords = word
        };

        var action = new AutomodAction
        {
            PlanetId = _planet.Id,
            MemberAddedBy = _ownerMember.Id,
            ActionType = AutomodActionType.BlockMessage,
            Message = string.Empty,
            Strikes = 1,
            UseGlobalStrikes = false
        };

        var createResult = await _automodService.CreateTriggerWithActionsAsync(trigger, [action]);
        Assert.True(createResult.Success, createResult.Message);
    }

    private async Task<User> RegisterNewUserAsync()
    {
        var details = await _fixture.RegisterUser();
        var dbUser = await _db.Users.FirstAsync(x => x.Name == details.Username);
        var user = dbUser.ToModel();
        _createdUsers.Add(user);
        return user;
    }

    private async Task<PlanetMember> RegisterAndJoinPlanetAsync()
    {
        var user = await RegisterNewUserAsync();
        var joinResult = await _memberService.AddMemberAsync(_planet.Id, user.Id);
        Assert.True(joinResult.Success, joinResult.Message);
        Assert.NotNull(joinResult.Data);
        return joinResult.Data!;
    }

    private async Task<Message?> WaitForMessageAsync(
        long channelId,
        Func<Message, bool> predicate,
        int timeoutMs = 5000)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            var staged = PlanetMessageWorker.GetStagedMessages(channelId);
            var match = staged.FirstOrDefault(predicate);
            if (match is not null)
                return match;

            // The worker may flush between polling iterations. Treat a
            // persisted response as success too; staging is an implementation
            // detail, while delivery is the actual behavior under test.
            var persisted = await _db.Messages
                .AsNoTracking()
                .Where(x => x.ChannelId == channelId)
                .ToListAsync();
            match = persisted.Select(x => x.ToModel()).FirstOrDefault(predicate);
            if (match is not null)
                return match;

            await Task.Delay(50);
        }

        return null;
    }
}
