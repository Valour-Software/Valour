using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Server.Workers;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

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

    public async Task InitializeAsync()
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

    public async Task DisposeAsync()
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

        var response = await WaitForStagedMessageAsync(_defaultChannel.Id, m =>
            m.AuthorUserId == ISharedUser.VictorUserId &&
            m.Content.Contains("Automod response text", StringComparison.Ordinal) &&
            m.Content.Contains($"«@m-{regularMember.Id}»", StringComparison.Ordinal));

        Assert.NotNull(response);
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

        var response = await WaitForStagedMessageAsync(_defaultChannel.Id, m =>
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

    private static async Task<Message?> WaitForStagedMessageAsync(
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

            await Task.Delay(50);
        }

        return null;
    }
}
