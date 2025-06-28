using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
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
    }

    public async Task InitializeAsync()
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

    public async Task DisposeAsync()
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

    private async Task<Valour.Shared.Models.User> RegisterNewUserAsync()
    {
        var details = await _fixture.RegisterUser();
        var user = await _db.Users.FirstAsync(u => u.Name == details.Username);
        return user.ToModel();
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
