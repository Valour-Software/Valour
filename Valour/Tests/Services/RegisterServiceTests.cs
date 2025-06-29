using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class RegisterServiceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IServiceScope _scope;
    private readonly RegisterService _registerService;
    private readonly UserService _userService;
    private readonly ValourDb _db;

    // Track users created during tests so they can be cleaned up
    private readonly List<User> _createdUsers = new();

    public RegisterServiceTests(LoginTestFixture fixture)
    {
        _factory = fixture.Factory;

        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _registerService = _scope.ServiceProvider.GetRequiredService<RegisterService>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var user in _createdUsers)
        {
            await _userService.HardDelete(user);
        }
    }

    private static RegisterUserRequest BuildValidRequest()
    {
        var uid = Guid.NewGuid().ToString()[..8];
        return new RegisterUserRequest
        {
            Email = $"register-{uid}@test.xyz",
            Username = $"register-{uid}",
            Password = $"Test-{uid}!",
            DateOfBirth = new DateTime(2000, 1, 1),
            Locality = Locality.General,
            Source = "test"
        };
    }

    [Fact]
    public async Task RegisterUser_SuccessCreatesUser()
    {
        var request = BuildValidRequest();
        var context = new DefaultHttpContext();

        var result = await _registerService.RegisterUserAsync(request, context, skipEmail: true);
        Assert.True(result.Success, result.Message);

        var created = await _userService.GetByNameAsync(request.Username);
        Assert.NotNull(created);
        _createdUsers.Add(created);

        var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == created.Id);
        Assert.NotNull(privateInfo);
        Assert.True(privateInfo.Verified);

        var onboarding = await _db.PlanetMembers.FirstOrDefaultAsync(x => x.UserId == created.Id && x.PlanetId == ISharedPlanet.ValourCentralId);
        Assert.NotNull(onboarding);

        var victorFriend = await _db.UserFriends.FirstOrDefaultAsync(x => x.UserId == created.Id && x.FriendId == ISharedUser.VictorUserId);
        Assert.NotNull(victorFriend);
    }

    [Fact]
    public async Task RegisterUser_UnderageFails()
    {
        var request = BuildValidRequest();
        request.DateOfBirth = DateTime.Today.AddYears(-10); // under 13
        var context = new DefaultHttpContext();

        var result = await _registerService.RegisterUserAsync(request, context, skipEmail: true);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task RegisterUser_DuplicateEmailFails()
    {
        var request1 = BuildValidRequest();
        var ctx = new DefaultHttpContext();

        var r1 = await _registerService.RegisterUserAsync(request1, ctx, skipEmail: true);
        Assert.True(r1.Success);
        var user1 = await _userService.GetByNameAsync(request1.Username);
        Assert.NotNull(user1);
        _createdUsers.Add(user1);

        var request2 = BuildValidRequest();
        request2.Email = request1.Email; // duplicate email

        var r2 = await _registerService.RegisterUserAsync(request2, ctx, skipEmail: true);
        Assert.False(r2.Success);
    }

    [Fact]
    public async Task RegisterUser_InvalidUsernameFails()
    {
        var request = BuildValidRequest();
        request.Username = new string('a', 40); // exceeds max length
        var ctx = new DefaultHttpContext();

        var result = await _registerService.RegisterUserAsync(request, ctx, skipEmail: true);
        Assert.False(result.Success);
    }
}

