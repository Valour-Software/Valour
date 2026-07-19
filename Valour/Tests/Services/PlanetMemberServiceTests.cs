using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Hubs;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class PlanetMemberServiceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ValourClient _client;
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _memberService;
    private readonly PlanetRoleService _roleService;
    private readonly UserService _userService;
    private readonly TokenService _tokenService;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly ValourDb _db;

    private Planet _planet = null!;
    private readonly List<long> _createdMembers = new();
    private readonly List<long> _createdRoles = new();

    public PlanetMemberServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _factory = fixture.Factory;

        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _tokenService = _scope.ServiceProvider.GetRequiredService<TokenService>();
        _connectionTracker = _scope.ServiceProvider.GetRequiredService<SignalRConnectionService>();
    }

    public async ValueTask InitializeAsync()
    {
        var owner = await _userService.GetAsync(_client.Me.Id);
        var createResult = await _planetService.CreateAsync(new Planet
        {
            Name = "Member Test Planet",
            Description = "Planet for testing members",
            OwnerId = owner.Id
        }, owner);
        Assert.True(createResult.Success, createResult.Message);
        _planet = createResult.Data!;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var memberId in _createdMembers)
        {
            await _memberService.DeleteAsync(memberId);
        }
        foreach (var roleId in _createdRoles)
        {
            await _roleService.DeleteAsync(_planet.Id, roleId);
        }
        if (_planet is not null)
            await _planetService.DeleteAsync(_planet.Id);
    }

    private async Task<User> RegisterNewUserAsync()
    {
        var details = await _fixture.RegisterUser();
        var user = await _db.Users.FirstAsync(u => u.Name == details.Username);
        return user.ToModel();
    }

    private sealed class TestHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal User { get; } = new(new ClaimsIdentity());
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    [Fact]
    public async Task AddMember_CreatesMember()
    {
        var newUser = await RegisterNewUserAsync();

        var result = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);

        _createdMembers.Add(result.Data.Id);

        var exists = await _memberService.ExistsAsync(result.Data.Id);
        Assert.True(exists);
    }

    [Fact]
    public async Task AddMember_AlreadyMemberFails()
    {
        var newUser = await RegisterNewUserAsync();
        var first = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(first.Success);
        _createdMembers.Add(first.Data.Id);

        var second = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.False(second.Success);
        Assert.Contains("Already a member", second.Message);
    }

    [Fact]
    public async Task UpdateMember_InvalidNicknameFails()
    {
        var member = await _memberService.GetByUserAsync(_client.Me.Id, _planet.Id);
        Assert.NotNull(member);

        member.Nickname = new string('x', 40); // too long
        var update = await _memberService.UpdateAsync(member);
        Assert.False(update.Success);
        Assert.Contains("Maximum nickname", update.Message);
    }

    [Fact]
    public async Task AddAndRemoveRoleUpdatesMembership()
    {
        var newUser = await RegisterNewUserAsync();
        var addResult = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(addResult.Success);
        var member = addResult.Data!;
        _createdMembers.Add(member.Id);

        var roleCreate = await _roleService.CreateAsync(new PlanetRole
        {
            Name = "Temp Role",
            PlanetId = _planet.Id,
            Color = "#010203"
        });
        Assert.True(roleCreate.Success, roleCreate.Message);
        var role = roleCreate.Data!;
        _createdRoles.Add(role.Id);

        var addRole = await _memberService.AddRoleAsync(_planet.Id, member.Id, role.Id);
        Assert.True(addRole.Success, addRole.Message);

        member = await _memberService.GetAsync(member.Id);
        Assert.True(await _memberService.HasRoleAsync(member, role.Id));

        var removeRole = await _memberService.RemoveRoleAsync(_planet.Id, member.Id, role.Id);
        Assert.True(removeRole.Success);

        member = await _memberService.GetAsync(member.Id);
        Assert.False(await _memberService.HasRoleAsync(member, role.Id));
    }

    [Fact]
    public async Task DeleteMember_SetsDeletedFlag()
    {
        var newUser = await RegisterNewUserAsync();
        var create = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(create.Success);
        var member = create.Data!;

        var del = await _memberService.DeleteAsync(member.Id);
        Assert.True(del.Success);

        var dbMember = await _db.PlanetMembers.IgnoreQueryFilters().FirstAsync(m => m.Id == member.Id);
        Assert.True(dbMember.IsDeleted);
    }

    [Fact]
    public async Task DeleteMember_EvictsExistingRealtimeSubscriptions()
    {
        var newUser = await RegisterNewUserAsync();
        var create = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(create.Success, create.Message);

        var channelId = await _db.Channels
            .Where(channel => channel.PlanetId == _planet.Id && channel.ChannelType == ChannelTypeEnum.PlanetChat)
            .Select(channel => channel.Id)
            .FirstAsync();

        var context = new TestHubCallerContext("membership-revocation-" + Guid.NewGuid());
        _connectionTracker.AddConnectionIdentity(context.ConnectionId, new AuthToken
        {
            Id = "membership-revocation-token-" + Guid.NewGuid(),
            UserId = newUser.Id,
        });

        try
        {
            await _connectionTracker.TrackGroupMembershipAsync($"p-{_planet.Id}", context, create.Data!.Id);
            await _connectionTracker.TrackGroupMembershipAsync($"c-{channelId}", context);
            await _connectionTracker.TrackGroupMembershipAsync($"i-{_planet.Id}", context);

            var deletion = await _memberService.DeleteAsync(create.Data.Id);
            Assert.True(deletion.Success, deletion.Message);

            Assert.DoesNotContain(context.ConnectionId, _connectionTracker.GetGroupConnections($"p-{_planet.Id}"));
            Assert.DoesNotContain(context.ConnectionId, _connectionTracker.GetGroupConnections($"c-{channelId}"));
            Assert.DoesNotContain(context.ConnectionId, _connectionTracker.GetGroupConnections($"i-{_planet.Id}"));
        }
        finally
        {
            await _connectionTracker.RemoveAllMembershipsAsync(context);
            _connectionTracker.RemoveConnectionIdentity(context.ConnectionId);
        }
    }

    [Fact]
    public async Task FederationSession_CannotJoinUserRealtimeGroup()
    {
        var token = new Valour.Database.AuthToken
        {
            Id = "fed-" + Guid.NewGuid().ToString("N"),
            AppId = "FEDERATION",
            UserId = _client.Me.Id,
            Scope = 0,
            TimeCreated = DateTime.UtcNow,
            TimeExpires = DateTime.UtcNow.AddMinutes(5),
            IssuedAddress = "test",
        };
        await _db.AuthTokens.AddAsync(token);
        await _db.SaveChangesAsync();

        var context = new TestHubCallerContext("federation-user-group-" + Guid.NewGuid());
        var hub = ActivatorUtilities.CreateInstance<CoreHub>(_scope.ServiceProvider);
        hub.Context = context;

        try
        {
            var authorization = await hub.Authorize(token.Id);
            Assert.True(authorization.Success, authorization.Message);

            var result = await hub.JoinUser(isPrimary: false);
            Assert.False(result.Success);
            Assert.DoesNotContain(context.ConnectionId, _connectionTracker.GetGroupConnections($"u-{token.UserId}"));

            await _connectionTracker.RemoveAllMembershipsAsync(context);
            Assert.Empty(_connectionTracker.GetConnectionsByTokenId(token.Id));
        }
        finally
        {
            _connectionTracker.RemoveConnectionIdentity(context.ConnectionId);
            _tokenService.RemoveFromQuickCache(token.Id);
            _db.AuthTokens.Remove(token);
            await _db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ExistsAndGetByUser_WorkCorrectly()
    {
        var newUser = await RegisterNewUserAsync();
        var create = await _memberService.AddMemberAsync(_planet.Id, newUser.Id);
        Assert.True(create.Success);
        var member = create.Data!;
        _createdMembers.Add(member.Id);

        var existsById = await _memberService.ExistsAsync(member.Id);
        Assert.True(existsById);

        var existsByUser = await _memberService.ExistsAsync(newUser.Id, _planet.Id);
        Assert.True(existsByUser);

        var fetched = await _memberService.GetByUserAsync(newUser.Id, _planet.Id);
        Assert.NotNull(fetched);
        Assert.Equal(member.Id, fetched.Id);
    }
}
