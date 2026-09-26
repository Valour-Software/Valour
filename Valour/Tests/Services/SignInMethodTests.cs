using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Mapping;
using Valour.Server.Services;
using Valour.Server.Services.ExternalAuth;
using Valour.Shared.Models;
using CredentialType = Valour.Database.CredentialType;
using User = Valour.Server.Models.User;

namespace Valour.Tests.Services;

/// <summary>
/// Sign-in methods beyond a password: the rule that an account keeps a way in,
/// identity proofs, fingerprint device keys, and Google or Discord sign-in
/// through a fake provider.
/// </summary>
[Collection("ApiCollection")]
public class SignInMethodTests : IAsyncLifetime
{
    private const string Password = "TempPass1!";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly RegisterService _registerService;
    private readonly SignInMethodService _signInMethods;
    private readonly AuthTicketStore _tickets;
    private readonly List<User> _createdUsers = new();

    public SignInMethodTests(LoginTestFixture fixture)
    {
        _factory = fixture.Factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _registerService = _scope.ServiceProvider.GetRequiredService<RegisterService>();
        _signInMethods = _scope.ServiceProvider.GetRequiredService<SignInMethodService>();
        _tickets = _scope.ServiceProvider.GetRequiredService<AuthTicketStore>();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var user in _createdUsers)
        {
            try { await _userService.HardDelete(user); }
            catch { /* ignore cleanup failures */ }
        }
        _scope.Dispose();
    }

    // Helpers //

    private static string RandomName() => Guid.NewGuid().ToString("N")[..10];

    private async Task<(User User, string Email)> RegisterPasswordUserAsync()
    {
        var name = RandomName();
        var email = $"t{name}@test.com";
        var result = await _registerService.RegisterUserAsync(new RegisterUserRequest
        {
            Username = $"signin-{name}",
            Email = email,
            Password = Password,
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test",
            IsNotTexasResident = true,
        }, NewHttpContext(), skipEmail: true);
        Assert.True(result.Success, result.Message);
        _createdUsers.Add(result.Data);
        return (result.Data, email);
    }

    private async Task<User> RegisterExternalUserAsync(ExternalIdentity identity)
    {
        var result = await _registerService.RegisterUserAsync(new RegisterUserRequest
        {
            Username = $"ext-{RandomName()}",
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test",
            IsNotTexasResident = true,
        }, NewHttpContext(), external: identity);
        Assert.True(result.Success, result.Message);
        _createdUsers.Add(result.Data);
        return result.Data;
    }

    private static ExternalIdentity NewGoogleIdentity(string email = null) => new(
        ExternalAuthProviders.Google,
        CredentialType.GOOGLE,
        "g-" + RandomName(),
        email ?? $"g{RandomName()}@gmail.test",
        true,
        null,
        "google account",
        null);

    private static DefaultHttpContext NewHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        return context;
    }

    private async Task<AuthResult> PostTokenAsync(TokenRequest request)
    {
        var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync("api/users/token", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    /// <summary>A provider that returns a fixed account instead of calling Google.</summary>
    private sealed class FakeGoogleProvider(ExternalIdentity identity)
        : ExternalAuthProvider(null!, NullLogger.Instance)
    {
        public override string Name => ExternalAuthProviders.Google;
        public override string DisplayName => "Google";
        public override string CredentialType => Valour.Database.CredentialType.GOOGLE;
        protected override ExternalAuthProviderConfig Config => new() { ClientId = "id", ClientSecret = "secret" };
        protected override string AuthorizeUrl => "https://accounts.example/authorize";
        protected override string TokenUrl => "https://accounts.example/token";
        protected override string Scope => "openid email";

        public override Task<ExternalIdentity> GetIdentityAsync(string code, string redirectUri) =>
            Task.FromResult(identity);

        protected override Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken) =>
            Task.FromResult(identity);
    }

    private ExternalAuthService NewExternalAuth(ExternalIdentity identity) => new(
        [new FakeGoogleProvider(identity)],
        _tickets,
        _signInMethods,
        _scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
        _db,
        NullLogger<ExternalAuthService>.Instance);

    /// <summary>Runs a whole web provider flow and returns its result.</summary>
    private async Task<(ExternalAuthResultResponse Result, string Verifier)> RunExternalFlowAsync(
        ExternalAuthService service, ExternalAuthIntent intent, long? userId = null, string reauthProof = null)
    {
        var verifier = AuthTicketStore.NewId();
        var begin = await service.BeginAsync(ExternalAuthProviders.Google, new ExternalAuthBeginRequest
        {
            Intent = intent,
            Client = ExternalAuthClient.Web,
            VerifierHash = AuthTicketStore.HashVerifier(verifier),
            ReauthProof = reauthProof,
        }, userId, NewHttpContext().Request);
        Assert.True(begin.Success, begin.Message);

        await service.HandleCallbackAsync(ExternalAuthProviders.Google, "code", begin.Data.FlowId, null);

        Assert.Null(await service.TakeWebResultAsync(begin.Data.FlowId, "wrong-verifier"));
        var result = await service.TakeWebResultAsync(begin.Data.FlowId, verifier);
        Assert.NotNull(result);
        return (result, verifier);
    }

    // Keeping a way in //

    [Fact]
    public async Task RemoveMethod_KeepsTheLastMethodThatWorksOnANewDevice()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var password = await _signInMethods.GetPasswordCredentialAsync(user.Id);

        var alone = await _signInMethods.RemoveMethodAsync(user.Id, password.Id);
        Assert.False(alone.Success);

        // A device key is lost with its device, so it doesn't count.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = await _signInMethods.RegisterDeviceKeyAsync(user.Id,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "Test phone");
        Assert.True(keyId.Success, keyId.Message);
        Assert.False((await _signInMethods.RemoveMethodAsync(user.Id, password.Id)).Success);

        _db.Credentials.Add(new Valour.Database.Credential
        {
            Id = Valour.Server.Database.IdManager.Generate(),
            UserId = user.Id,
            CredentialType = CredentialType.DISCORD,
            Identifier = "d-" + RandomName(),
            DisplayName = "@tester",
        });
        await _db.SaveChangesAsync();

        Assert.True((await _signInMethods.RemoveMethodAsync(user.Id, password.Id)).Success);
        Assert.Null(await _signInMethods.GetPasswordCredentialAsync(user.Id));
    }

    [Fact]
    public async Task AddPassword_GivesALinkedOnlyAccountAPassword()
    {
        var identity = NewGoogleIdentity();
        var user = await RegisterExternalUserAsync(identity);
        Assert.Null(await _signInMethods.GetPasswordCredentialAsync(user.Id));

        Assert.True((await _signInMethods.AddPasswordAsync(user.Id, Password)).Success);
        Assert.False((await _signInMethods.AddPasswordAsync(user.Id, Password)).Success);

        var signIn = await _userService.ValidateCredentialAsync(CredentialType.PASSWORD, identity.Email, Password);
        Assert.True(signIn.Success, signIn.Message);
        Assert.Equal(user.Id, signIn.Data.Id);
    }

    [Fact]
    public async Task ConfirmIdentity_AcceptsThePasswordOrThatUsersProof()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var (other, _) = await RegisterPasswordUserAsync();

        Assert.True((await _signInMethods.ConfirmIdentityAsync(user.Id, Password, null)).Success);
        Assert.False((await _signInMethods.ConfirmIdentityAsync(user.Id, "wrong password", null)).Success);

        var proof = await _signInMethods.CreateReauthProofAsync(user.Id);
        Assert.True((await _signInMethods.ConfirmIdentityAsync(user.Id, null, proof.Proof)).Success);
        Assert.False((await _signInMethods.ConfirmIdentityAsync(other.Id, null, proof.Proof)).Success);
        Assert.False((await _signInMethods.ConfirmIdentityAsync(user.Id, null, "not-a-proof")).Success);
    }

    // Fingerprint device keys //

    [Fact]
    public async Task DeviceKey_SignsInOncePerChallenge()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = (await _signInMethods.RegisterDeviceKeyAsync(user.Id,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "Test phone")).Data;

        var challenge = (await _signInMethods.CreateDeviceChallengeAsync(keyId)).Data;
        var signature = Convert.ToBase64String(key.SignData(
            Convert.FromBase64String(challenge.Challenge), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        var request = new TokenRequest { DeviceKeyId = keyId, DeviceChallengeId = challenge.ChallengeId, DeviceSignature = signature };
        var first = await PostTokenAsync(request);
        Assert.True(first.Success, first.Message);
        Assert.Equal(user.Id, first.Token!.UserId);

        var replay = await PostTokenAsync(request);
        Assert.False(replay.Success);

        // A different key can't sign for this one.
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var second = (await _signInMethods.CreateDeviceChallengeAsync(keyId)).Data;
        var forged = await PostTokenAsync(new TokenRequest
        {
            DeviceKeyId = keyId,
            DeviceChallengeId = second.ChallengeId,
            DeviceSignature = Convert.ToBase64String(otherKey.SignData(
                Convert.FromBase64String(second.Challenge), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)),
        });
        Assert.False(forged.Success);
    }

    [Fact]
    public async Task PasswordRecovery_AddsAPasswordAndRemovesDeviceKeys()
    {
        var identity = NewGoogleIdentity();
        var user = await RegisterExternalUserAsync(identity);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await _signInMethods.RegisterDeviceKeyAsync(user.Id, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "Test phone");

        var code = Guid.NewGuid().ToString();
        _db.PasswordRecoveries.Add(new Valour.Database.PasswordRecovery { Code = code, UserId = user.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _userService.RecoveryUserAsync(
            new PasswordRecoveryRequest { Code = code, Password = Password },
            (await _db.PasswordRecoveries.FindAsync(code))!.ToModel(),
            null);
        Assert.True(result.Success, result.Message);

        _db.ChangeTracker.Clear();
        Assert.NotNull(await _signInMethods.GetPasswordCredentialAsync(user.Id));
        Assert.False(await _db.Credentials.AnyAsync(x => x.UserId == user.Id && x.CredentialType == CredentialType.DEVICE_KEY));
    }

    // Google and Discord //

    [Fact]
    public async Task ExternalSignIn_NewAccountRegistersThroughTheRegisterRoute()
    {
        var identity = NewGoogleIdentity();
        var (result, verifier) = await RunExternalFlowAsync(NewExternalAuth(identity), ExternalAuthIntent.Login);
        Assert.Equal(ExternalAuthResults.Register, result.Result);

        var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync("api/users/register", new RegisterUserRequest
        {
            Username = $"ext-{RandomName()}",
            DateOfBirth = new DateTime(2000, 1, 1),
            IsNotTexasResident = true,
            ExternalTicket = result.Ticket,
            ExternalVerifier = verifier,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.True(auth.Success, auth.Message);

        var created = (await _userService.GetAsync(auth.Token!.UserId))!;
        _createdUsers.Add(created);

        var credential = await _db.Credentials.AsNoTracking().SingleAsync(x => x.UserId == created.Id);
        Assert.Equal(CredentialType.GOOGLE, credential.CredentialType);
        Assert.Equal(identity.ProviderUserId, credential.Identifier);
        Assert.True((await _userService.GetUserPrivateInfoAsync(created.Id)).Verified);
    }

    [Fact]
    public async Task ExternalSignIn_LinkedAccountSignsInWithTheTicket()
    {
        var identity = NewGoogleIdentity();
        var user = await RegisterExternalUserAsync(identity);

        var (result, verifier) = await RunExternalFlowAsync(NewExternalAuth(identity), ExternalAuthIntent.Login);
        Assert.Equal(ExternalAuthResults.Login, result.Result);

        Assert.False((await PostTokenAsync(new TokenRequest { ExternalTicket = result.Ticket, ExternalVerifier = "wrong" })).Success);

        var auth = await PostTokenAsync(new TokenRequest { ExternalTicket = result.Ticket, ExternalVerifier = verifier });
        Assert.True(auth.Success, auth.Message);
        Assert.Equal(user.Id, auth.Token!.UserId);

        // The ticket is spent once sign-in completes.
        Assert.False((await PostTokenAsync(new TokenRequest { ExternalTicket = result.Ticket, ExternalVerifier = verifier })).Success);
    }

    [Fact]
    public async Task ExternalSignIn_VerifiedEmailOfAnotherAccountIsNotTakenOver()
    {
        var (_, email) = await RegisterPasswordUserAsync();
        var (result, _) = await RunExternalFlowAsync(NewExternalAuth(NewGoogleIdentity(email)), ExternalAuthIntent.Login);

        Assert.Equal(ExternalAuthResults.Error, result.Result);
        Assert.Contains("already exists", result.Message);
    }

    [Fact]
    public async Task ExternalLink_NeedsAProofAndOneOwner()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var identity = NewGoogleIdentity();
        var service = NewExternalAuth(identity);

        var withoutProof = await service.BeginAsync(ExternalAuthProviders.Google, new ExternalAuthBeginRequest
        {
            Intent = ExternalAuthIntent.Link,
            Client = ExternalAuthClient.Web,
            VerifierHash = AuthTicketStore.HashVerifier(AuthTicketStore.NewId()),
        }, user.Id, NewHttpContext().Request);
        Assert.False(withoutProof.Success);

        var proof = (await _signInMethods.CreateReauthProofAsync(user.Id)).Proof;
        var (linked, _) = await RunExternalFlowAsync(service, ExternalAuthIntent.Link, user.Id, proof);
        Assert.Equal(ExternalAuthResults.Linked, linked.Result);
        Assert.True(await _db.Credentials.AnyAsync(x => x.UserId == user.Id && x.Identifier == identity.ProviderUserId));

        // The same Google account can't be linked to a second Valour account.
        var (other, _) = await RegisterPasswordUserAsync();
        var otherProof = (await _signInMethods.CreateReauthProofAsync(other.Id)).Proof;
        var (refused, _) = await RunExternalFlowAsync(service, ExternalAuthIntent.Link, other.Id, otherProof);
        Assert.Equal(ExternalAuthResults.Error, refused.Result);

        // Signing in again with the linked account confirms identity.
        var (reauth, _) = await RunExternalFlowAsync(service, ExternalAuthIntent.Reauth, user.Id);
        Assert.Equal(ExternalAuthResults.Reauth, reauth.Result);
        Assert.True((await _signInMethods.ConfirmIdentityAsync(user.Id, null, reauth.Ticket)).Success);
    }
}
