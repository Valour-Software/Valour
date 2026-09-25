using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Models;
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

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
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
            Source = "test",
            IsNotTexasResident = true
        };
    }

    [Fact]
    public async Task RegisterUser_TexasResidencyAttestationRequired()
    {
        var request = BuildValidRequest();
        request.IsNotTexasResident = false;

        var result = await _registerService.RegisterUserAsync(request, new DefaultHttpContext(), skipEmail: true);

        Assert.False(result.Success);
        Assert.Equal("New registrations are not available to legal residents of Texas. Visit valour.gg/texas to learn why.", result.Message);
    }

    [Fact]
    public async Task RegisterUser_SuccessCreatesUser()
    {
        var request = BuildValidRequest();
        var context = new DefaultHttpContext();

        var result = await _registerService.RegisterUserAsync(request, context, skipEmail: true);
        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);

        var created = await _userService.GetByNameAndTagAsync(result.Data.NameAndTag);
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
        Assert.NotNull(r1.Data);
        
        var user1 = await _userService.GetByNameAndTagAsync(r1.Data.NameAndTag);
        Assert.NotNull(user1);
        _createdUsers.Add(user1);

        var request2 = BuildValidRequest();
        request2.Email = request1.Email; // duplicate email

        var r2 = await _registerService.RegisterUserAsync(request2, ctx, skipEmail: true);
        Assert.False(r2.Success);
    }

    [Fact]
    public async Task RegisterUser_ReplacesAbandonedUnverifiedAccount()
    {
        var ctx = new DefaultHttpContext();
        var first = BuildValidRequest();

        var r1 = await _registerService.RegisterUserAsync(first, ctx, skipEmail: true);
        Assert.True(r1.Success, r1.Message);
        var staleId = r1.Data.Id;

        // Simulate an account whose confirmation link was never used and expired
        var info = await _db.PrivateInfos.FirstAsync(x => x.UserId == staleId);
        info.Verified = false;
        await _db.SaveChangesAsync();

        var second = BuildValidRequest();
        second.Email = first.Email;

        var r2 = await _registerService.RegisterUserAsync(second, ctx, skipEmail: true);
        Assert.True(r2.Success, r2.Message);
        _createdUsers.Add(r2.Data);

        Assert.False(await _db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == staleId));
        var credential = await _db.Credentials.FirstAsync(x => x.Identifier == first.Email);
        Assert.Equal(r2.Data.Id, credential.UserId);
    }

    [Fact]
    public async Task RegisterUser_KeepsUnverifiedAccountWithLiveConfirmationCode()
    {
        var ctx = new DefaultHttpContext();
        var first = BuildValidRequest();

        var r1 = await _registerService.RegisterUserAsync(first, ctx, skipEmail: true);
        Assert.True(r1.Success, r1.Message);
        _createdUsers.Add(r1.Data);

        var info = await _db.PrivateInfos.FirstAsync(x => x.UserId == r1.Data.Id);
        info.Verified = false;
        _db.EmailConfirmCodes.Add(new Valour.Database.EmailConfirmCode
        {
            Code = Guid.NewGuid().ToString(),
            UserId = r1.Data.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _db.SaveChangesAsync();

        var second = BuildValidRequest();
        second.Email = first.Email;

        var r2 = await _registerService.RegisterUserAsync(second, ctx, skipEmail: true);
        Assert.False(r2.Success);
        Assert.Equal(RegisterService.EmailAlreadyRegisteredCode, r2.Message);
        Assert.True(await _db.Users.AnyAsync(x => x.Id == r1.Data.Id));
    }

    [Fact]
    public async Task RegisterUser_ValidatesFieldsBeforeCheckingEmail()
    {
        var ctx = new DefaultHttpContext();
        var first = BuildValidRequest();

        var r1 = await _registerService.RegisterUserAsync(first, ctx, skipEmail: true);
        Assert.True(r1.Success, r1.Message);
        _createdUsers.Add(r1.Data);

        // A bad password gets the same answer whether or not the email is taken
        var taken = BuildValidRequest();
        taken.Email = first.Email;
        taken.Password = "short";
        var fresh = BuildValidRequest();
        fresh.Password = "short";

        var takenResult = await _registerService.RegisterUserAsync(taken, ctx, skipEmail: true);
        var freshResult = await _registerService.RegisterUserAsync(fresh, ctx, skipEmail: true);
        Assert.False(takenResult.Success);
        Assert.Equal(freshResult.Message, takenResult.Message);
    }

    [Fact]
    public async Task VerifyEmail_PaysReferralRewardOnce()
    {
        var ctx = new DefaultHttpContext();

        var referrerRequest = BuildValidRequest();
        var referrer = await _registerService.RegisterUserAsync(referrerRequest, ctx, skipEmail: true);
        Assert.True(referrer.Success, referrer.Message);
        _createdUsers.Add(referrer.Data);

        var referred = await _registerService.RegisterUserAsync(BuildValidRequest(), ctx, skipEmail: true);
        Assert.True(referred.Success, referred.Message);
        _createdUsers.Add(referred.Data);

        // An unverified account referred by the referrer, waiting on its email
        var info = await _db.PrivateInfos.FirstAsync(x => x.UserId == referred.Data.Id);
        info.Verified = false;
        _db.Referrals.Add(new Valour.Database.Referral
        {
            UserId = referred.Data.Id,
            ReferrerId = referrer.Data.Id,
            Created = DateTime.UtcNow,
            Reward = 0
        });
        var code = Guid.NewGuid().ToString();
        _db.EmailConfirmCodes.Add(new Valour.Database.EmailConfirmCode
        {
            Code = code,
            UserId = referred.Data.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _db.SaveChangesAsync();

        var verify = await _userService.VerifyAsync(code);
        Assert.True(verify.Success, verify.Message);

        var referral = await _db.Referrals.AsNoTracking().FirstAsync(x => x.UserId == referred.Data.Id);
        Assert.Equal(50m, referral.Reward);

        var account = await _db.EcoAccounts.AsNoTracking().FirstAsync(x =>
            x.UserId == referrer.Data.Id &&
            x.CurrencyId == Valour.Shared.Models.Economy.ISharedCurrency.ValourCreditsId &&
            x.AccountType == Valour.Shared.Models.Economy.AccountType.User);
        Assert.Equal(50m, account.BalanceValue);

        // Paying again is a no-op
        await _userService.PayReferralRewardAsync(referred.Data.Id);
        account = await _db.EcoAccounts.AsNoTracking().FirstAsync(x => x.Id == account.Id);
        Assert.Equal(50m, account.BalanceValue);
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
