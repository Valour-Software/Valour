using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class NotificationServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly NotificationService _notificationService;
    private readonly PlanetService _planetService;
    private readonly ChannelService _channelService;
    private readonly PlanetMemberService _memberService;
    private readonly PlanetPermissionService _permissionService;

    private readonly long _valourCentralId = ISharedPlanet.ValourCentralId;

    public NotificationServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _factory = fixture.Factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _notificationService = _scope.ServiceProvider.GetRequiredService<NotificationService>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _channelService = _scope.ServiceProvider.GetRequiredService<ChannelService>();
        _memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _permissionService = _scope.ServiceProvider.GetRequiredService<PlanetPermissionService>();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<PlanetMember> AddRestrictedMemberAsync()
    {
        var details = await _fixture.RegisterUser();
        var user = await _db.Users.FirstAsync(x => x.Name == details.Username);

        // New users are auto-joined to Valour Central
        var member = await _memberService.GetByUserAsync(user.Id, _valourCentralId);
        if (member is null)
        {
            var addResult = await _memberService.AddMemberAsync(_valourCentralId, user.Id);
            Assert.True(addResult.Success, addResult.Message);
            member = addResult.Data;
        }

        return member;
    }

    private async Task<Channel> CreateDefaultRoleDeniedChannelAsync()
    {
        var defaultRole = await _planetService.GetDefaultRole(_valourCentralId);
        Assert.NotNull(defaultRole);

        var createResult = await _channelService.CreateAsync(new Channel()
        {
            Name = $"hidden-{Guid.NewGuid():N}"[..12],
            PlanetId = _valourCentralId,
            ParentId = null,
            ChannelType = ChannelTypeEnum.PlanetChat,
            Description = "Hidden from the default role",
            InheritsPerms = false,
            RawPosition = 0
        }, new List<PermissionsNode>()
        {
            new PermissionsNode()
            {
                PlanetId = defaultRole.PlanetId,
                RoleId = defaultRole.Id,
                Mask = Permission.FULL_CONTROL,
                Code = 0 // Deny everything, including View
            }
        });

        Assert.True(createResult.Success, createResult.Message);
        return createResult.Data;
    }

    [Fact]
    public async Task MemberMention_InInaccessibleChannel_DoesNotNotify()
    {
        // Regression for #1570: members were notified for mentions in channels
        // they have no permission to view
        var planet = await _planetService.GetAsync(_valourCentralId);
        var authorMember = await _memberService.GetByUserAsync(_client.Me.Id, _valourCentralId);
        Assert.NotNull(authorMember);

        var targetMember = await AddRestrictedMemberAsync();
        var hiddenChannel = await CreateDefaultRoleDeniedChannelAsync();

        // Sanity: the target member cannot see the channel
        Assert.False(await _permissionService.HasChannelAccessAsync(targetMember.Id, hiddenChannel.Id));

        var message = new Message()
        {
            Id = IdManager.Generate(),
            ChannelId = hiddenChannel.Id,
            PlanetId = _valourCentralId,
            AuthorUserId = _client.Me.Id,
            AuthorMemberId = authorMember.Id,
            Content = "psst, secret ping"
        };

        var mention = new Mention()
        {
            Type = MentionType.PlanetMember,
            TargetId = targetMember.Id
        };

        await _notificationService.HandleMentionAsync(
            mention, planet, message, authorMember, _client.Me, hiddenChannel);

        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == targetMember.UserId && x.ChannelId == hiddenChannel.Id));
    }

    [Fact]
    public async Task MemberMention_InAccessibleChannel_StillNotifies()
    {
        // Positive control for the #1570 fix: mentions in channels the member
        // CAN see must keep producing notifications
        var planet = await _planetService.GetAsync(_valourCentralId);
        var authorMember = await _memberService.GetByUserAsync(_client.Me.Id, _valourCentralId);
        Assert.NotNull(authorMember);

        var targetMember = await AddRestrictedMemberAsync();

        var publicChannel = await _db.Channels
            .Where(x => x.PlanetId == _valourCentralId && x.IsDefault)
            .Select(x => x.ToModel())
            .FirstAsync();

        Assert.True(await _permissionService.HasChannelAccessAsync(targetMember.Id, publicChannel.Id));

        var message = new Message()
        {
            Id = IdManager.Generate(),
            ChannelId = publicChannel.Id,
            PlanetId = _valourCentralId,
            AuthorUserId = _client.Me.Id,
            AuthorMemberId = authorMember.Id,
            Content = "hello there"
        };

        var mention = new Mention()
        {
            Type = MentionType.PlanetMember,
            TargetId = targetMember.Id
        };

        await _notificationService.HandleMentionAsync(
            mention, planet, message, authorMember, _client.Me, publicChannel);

        Assert.True(await _db.Notifications.AnyAsync(x =>
            x.UserId == targetMember.UserId && x.ChannelId == publicChannel.Id));
    }

    [Fact]
    public async Task RoleMention_InInaccessibleChannel_SkipsMembersWithoutAccess()
    {
        // Regression for #1570 (role/@everyone path): the fanout must exclude
        // role holders who cannot view the channel
        var targetMember = await AddRestrictedMemberAsync();
        var hiddenChannel = await CreateDefaultRoleDeniedChannelAsync();

        Assert.False(await _permissionService.HasChannelAccessAsync(targetMember.Id, hiddenChannel.Id));

        var defaultRole = await _planetService.GetDefaultRole(_valourCentralId);

        var baseNotification = new Notification()
        {
            Id = Guid.NewGuid(),
            Title = "Role mention test",
            Body = "role ping",
            PlanetId = _valourCentralId,
            ChannelId = hiddenChannel.Id,
            Source = NotificationSource.PlanetRoleMention,
            SourceId = IdManager.Generate()
        };

        await _notificationService.SendRoleNotificationsAsync(defaultRole.Id, baseNotification);

        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == targetMember.UserId && x.ChannelId == hiddenChannel.Id));
    }

    [Fact]
    public async Task RoleMention_InAccessibleChannel_NotifiesRoleHolders()
    {
        // Positive control: role mentions in a visible channel still fan out
        var targetMember = await AddRestrictedMemberAsync();

        var publicChannel = await _db.Channels
            .Where(x => x.PlanetId == _valourCentralId && x.IsDefault)
            .Select(x => x.ToModel())
            .FirstAsync();

        var defaultRole = await _planetService.GetDefaultRole(_valourCentralId);

        var baseNotification = new Notification()
        {
            Id = Guid.NewGuid(),
            Title = "Role mention test",
            Body = "role ping",
            PlanetId = _valourCentralId,
            ChannelId = publicChannel.Id,
            Source = NotificationSource.PlanetRoleMention,
            SourceId = IdManager.Generate()
        };

        await _notificationService.SendRoleNotificationsAsync(defaultRole.Id, baseNotification);

        Assert.True(await _db.Notifications.AnyAsync(x =>
            x.UserId == targetMember.UserId && x.ChannelId == publicChannel.Id));
    }
}
