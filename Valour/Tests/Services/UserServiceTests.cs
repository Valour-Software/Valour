using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

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
        var result = await _userService.GetByNameAsync($"{me.Name}#{me.Tag}");
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
    public async Task SetTutorialStepFinished_SetsBit()
    {
        var me = _client.Me;
        var before = (await _db.Users.FindAsync(me.Id)).TutorialState;
        var result = await _userService.SetTutorialStepFinishedAsync(me.Id, 2, true);
        Assert.True(result.Success);
        var after = (await _db.Users.FindAsync(me.Id)).TutorialState;
        Assert.Equal(before | (1u << 2), after);
    }

    [Fact]
    public void GetYearsOld_CalculatesCorrectly()
    {
        var birthday = DateTime.Today.AddYears(-20).AddDays(-1);
        var age = _userService.GetYearsOld(birthday);
        Assert.Equal(20, age);
    }

    [Fact]
    public async Task HardDelete_RemovesUser()
    {
        var req = new RegisterUserRequest
        {
            Username = $"temp-{Guid.NewGuid():N}".Substring(0, 8),
            Email = $"temp-{Guid.NewGuid():N}@example.com".Substring(0, 18),
            Password = "TempPass1!",
            DateOfBirth = new DateTime(2000, 1, 1),
            Locality = Locality.General,
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

        var del = await _userService.HardDelete(model);
        Assert.True(del.Success);
        _createdUsers.Remove(model);

        var check = await _db.Users.FindAsync(model.Id);
        Assert.Null(check);
    }

    [Fact]
    public async Task SetUserComplianceData_FailsForUnderage()
    {
        var me = _client.Me;
        var birth = DateTime.Today.AddYears(-10);
        var result = await _userService.SetUserComplianceData(me.Id, birth, Locality.General);
        Assert.False(result.Success);
    }
}
