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
    private readonly List<IServiceScope> _sessionScopes = new();

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
        foreach (var scope in _sessionScopes)
            scope.Dispose();
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

    /// <summary>Creates a signed-in Valour session for the user.</summary>
    private async Task<string> NewSessionAsync(long userId)
    {
        var id = "val-" + Guid.NewGuid();
        _db.AuthTokens.Add(NewToken(id, TokenService.SessionAppId, userId, DateTime.UtcNow.AddDays(7)));
        await _db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Sign-in method services as a request from <paramref name="sessionId"/>
    /// sees them. Kept synchronous so the request context stays set for the
    /// caller. Use the returned services before switching to another session.
    /// </summary>
    private SignInMethodService AsSession(string sessionId)
    {
        var context = NewHttpContext();
        context.Request.Headers.Authorization = sessionId;
        _factory.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var scope = _factory.Services.CreateScope();
        _sessionScopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<SignInMethodService>();
    }

    private async Task<HttpResponseMessage> PostAsSessionAsync(string sessionId, string uri, object body)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("authorization", sessionId);
        return await http.PostAsJsonAsync(uri, body);
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

    private ExternalAuthService NewExternalAuth(ExternalIdentity identity, SignInMethodService signInMethods = null) => new(
        [new FakeGoogleProvider(identity)],
        _tickets,
        signInMethods ?? _signInMethods,
        _scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
        _db,
        NullLogger<ExternalAuthService>.Instance);

    /// <summary>
    /// Runs a whole web provider flow and returns its result. The callback
    /// arrives with the browser binding cookie unless <paramref name="bindingCookie"/>
    /// replaces it (an empty string sends none).
    /// </summary>
    private async Task<(ExternalAuthResultResponse Result, string Verifier)> RunExternalFlowAsync(
        ExternalAuthService service, ExternalAuthIntent intent, long? userId = null, string reauthProof = null,
        string bindingCookie = null)
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
        var flowId = begin.Data.Response.FlowId;
        Assert.NotNull(begin.Data.BrowserBindingSecret);

        var callback = NewHttpContext();
        var cookie = bindingCookie ?? begin.Data.BrowserBindingSecret;
        if (cookie.Length > 0)
            callback.Request.Headers.Cookie = $"{ExternalAuthService.BrowserBindingCookieName(flowId)}={cookie}";

        await service.HandleCallbackAsync(ExternalAuthProviders.Google, "code", flowId, null, callback);

        Assert.Null(await service.TakeWebResultAsync(flowId, "wrong-verifier"));
        var result = await service.TakeWebResultAsync(flowId, verifier);
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
    public async Task ConfirmIdentity_AcceptsThePasswordOrAProofFromTheSameSession()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var (other, _) = await RegisterPasswordUserAsync();
        var session = await NewSessionAsync(user.Id);
        var secondSession = await NewSessionAsync(user.Id);

        var signIn = AsSession(session);
        Assert.True((await signIn.ConfirmIdentityAsync(user.Id, Password, null)).Success);
        Assert.False((await signIn.ConfirmIdentityAsync(user.Id, "wrong password", null)).Success);

        var proof = await signIn.CreateReauthProofAsync(user.Id);
        Assert.True((await signIn.ConfirmIdentityAsync(user.Id, null, proof.Proof)).Success);
        Assert.False((await signIn.ConfirmIdentityAsync(other.Id, null, proof.Proof)).Success);
        Assert.False((await signIn.ConfirmIdentityAsync(user.Id, null, "not-a-proof")).Success);

        // A proof that leaks is no use from another session, even the same user's.
        var fromSecondSession = AsSession(secondSession);
        Assert.False((await fromSecondSession.ConfirmIdentityAsync(user.Id, null, proof.Proof)).Success);
    }

    // Sessions //

    [Fact]
    public async Task UsingASession_RestartsItsLifetime_ButNotOtherTokens()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var soon = DateTime.UtcNow.AddDays(1);
        var session = "val-" + Guid.NewGuid();
        var bot = "bot-" + Guid.NewGuid();
        var app = "app-" + Guid.NewGuid();
        var botExpiry = DateTime.UtcNow.AddYears(100);

        _db.AuthTokens.AddRange(
            NewToken(session, TokenService.SessionAppId, user.Id, soon),
            NewToken(bot, "BOT", user.Id, botExpiry),
            NewToken(app, "12345", user.Id, soon));
        await _db.SaveChangesAsync();

        var tokens = _scope.ServiceProvider.GetRequiredService<TokenService>();
        Assert.NotNull(await tokens.GetAsync(session));
        Assert.NotNull(await tokens.GetAsync(bot));
        Assert.NotNull(await tokens.GetAsync(app));

        _db.ChangeTracker.Clear();
        var expiries = await _db.AuthTokens.AsNoTracking()
            .Where(x => x.Id == session || x.Id == bot || x.Id == app)
            .ToDictionaryAsync(x => x.Id, x => x.TimeExpires);

        Assert.True(expiries[session] > DateTime.UtcNow.AddDays(6.9), "A used session gets the full lifetime again.");
        Assert.True(expiries[bot] > DateTime.UtcNow.AddYears(99), "Bot tokens are never shortened.");
        Assert.True(expiries[app] < DateTime.UtcNow.AddDays(1.1), "OAuth app tokens keep their fixed expiry.");
    }

    [Fact]
    public async Task Sessions_StopRenewingAtTheirMaximumAge()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var id = "val-" + Guid.NewGuid();
        var token = NewToken(id, TokenService.SessionAppId, user.Id, DateTime.UtcNow.AddDays(1));
        token.TimeCreated = DateTime.UtcNow - TokenService.MaxSessionAge + TimeSpan.FromDays(3);
        _db.AuthTokens.Add(token);
        await _db.SaveChangesAsync();

        Assert.NotNull(await _scope.ServiceProvider.GetRequiredService<TokenService>().GetAsync(id));

        var expires = await _db.AuthTokens.AsNoTracking().Where(x => x.Id == id).Select(x => x.TimeExpires).FirstAsync();
        Assert.True(expires < DateTime.UtcNow.AddDays(3.1), "Renewal stops at the maximum age.");
        Assert.True(expires > DateTime.UtcNow.AddDays(2.9), "Renewal still extends up to the maximum age.");
    }

    private static Valour.Database.AuthToken NewToken(string id, string appId, long userId, DateTime expires) => new()
    {
        Id = id,
        AppId = appId,
        UserId = userId,
        Scope = -1,
        TimeCreated = DateTime.UtcNow.AddDays(-6),
        TimeExpires = expires,
        IssuedAddress = "127.0.0.1",
    };

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
    public async Task SigningIn_GivesTheNewSessionAProofForTurningOnFingerprint()
    {
        var (user, email) = await RegisterPasswordUserAsync();
        var signIn = await PostTokenAsync(new TokenRequest { Email = email, Password = Password });
        Assert.True(signIn.Success, signIn.Message);
        Assert.False(string.IsNullOrEmpty(signIn.ReauthProof));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new DeviceKeyRegisterRequest
        {
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            DeviceName = "Test phone",
            ReauthProof = signIn.ReauthProof,
        };

        // The proof belongs to the session the sign-in created.
        var otherSession = await NewSessionAsync(user.Id);
        Assert.False((await PostAsSessionAsync(otherSession, "api/auth/device/register", request)).IsSuccessStatusCode);

        var registered = await PostAsSessionAsync(signIn.Token!.Id, "api/auth/device/register", request);
        Assert.True(registered.IsSuccessStatusCode, await registered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProofFromATwoFactorSignIn_StandsInForTheCodeOnlyInItsSession()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var session = await NewSessionAsync(user.Id);
        var otherSession = await NewSessionAsync(user.Id);

        var signIn = AsSession(session);
        var withCode = (await signIn.CreateReauthProofAsync(user.Id, session, true)).Proof;
        var withoutCode = (await signIn.CreateReauthProofAsync(user.Id, session, false)).Proof;
        Assert.True(await signIn.ProofIncludesMultiFactorAsync(user.Id, withCode));
        Assert.False(await signIn.ProofIncludesMultiFactorAsync(user.Id, withoutCode));

        var fromOtherSession = AsSession(otherSession);
        Assert.False(await fromOtherSession.ProofIncludesMultiFactorAsync(user.Id, withCode));
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
    public async Task ExternalSignIn_OnlyFinishesInTheBrowserThatStartedIt()
    {
        // Someone who starts a sign-in and sends the provider link to another
        // person must not receive a sign-in for that person's account.
        var identity = NewGoogleIdentity();
        await RegisterExternalUserAsync(identity);
        var service = NewExternalAuth(identity);

        var (withoutCookie, _) = await RunExternalFlowAsync(service, ExternalAuthIntent.Login, bindingCookie: "");
        Assert.Equal(ExternalAuthResults.Error, withoutCookie.Result);
        Assert.Null(withoutCookie.Ticket);

        var (wrongCookie, _) = await RunExternalFlowAsync(service, ExternalAuthIntent.Login, bindingCookie: AuthTicketStore.NewId());
        Assert.Equal(ExternalAuthResults.Error, wrongCookie.Result);
        Assert.Null(wrongCookie.Ticket);
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
        var signIn = AsSession(await NewSessionAsync(user.Id));
        var service = NewExternalAuth(identity, signIn);

        var withoutProof = await service.BeginAsync(ExternalAuthProviders.Google, new ExternalAuthBeginRequest
        {
            Intent = ExternalAuthIntent.Link,
            Client = ExternalAuthClient.Web,
            VerifierHash = AuthTicketStore.HashVerifier(AuthTicketStore.NewId()),
        }, user.Id, NewHttpContext().Request);
        Assert.False(withoutProof.Success);

        var proof = (await signIn.CreateReauthProofAsync(user.Id)).Proof;
        var (linked, verifier) = await RunExternalFlowAsync(service, ExternalAuthIntent.Link, user.Id, proof);
        Assert.Equal(ExternalAuthResults.Linked, linked.Result);
        Assert.NotNull(linked.Ticket);

        // Nothing is linked until the client that started the flow redeems it
        // for the same user with its verifier.
        Assert.False(await _db.Credentials.AnyAsync(x => x.Identifier == identity.ProviderUserId));
        var (other, _) = await RegisterPasswordUserAsync();
        Assert.False((await service.RedeemLinkAsync(linked.Ticket, verifier, other.Id)).Success);
        Assert.False((await service.RedeemLinkAsync(linked.Ticket, "wrong-verifier", user.Id)).Success);

        Assert.True((await service.RedeemLinkAsync(linked.Ticket, verifier, user.Id)).Success);
        Assert.True(await _db.Credentials.AnyAsync(x => x.UserId == user.Id && x.Identifier == identity.ProviderUserId));
        Assert.False((await service.RedeemLinkAsync(linked.Ticket, verifier, user.Id)).Success);

        // The same Google account can't be linked to a second Valour account.
        var otherSignIn = AsSession(await NewSessionAsync(other.Id));
        var otherProof = (await otherSignIn.CreateReauthProofAsync(other.Id)).Proof;
        var (refused, _) = await RunExternalFlowAsync(NewExternalAuth(identity, otherSignIn), ExternalAuthIntent.Link, other.Id, otherProof);
        Assert.Equal(ExternalAuthResults.Error, refused.Result);
    }

    [Fact]
    public async Task ExternalLink_FromAnAppLinksNothingAtTheCallback()
    {
        // App results are sent to the device, so a provider link sent to
        // someone else must not link their account to the sender's.
        var (user, _) = await RegisterPasswordUserAsync();
        var identity = NewGoogleIdentity();
        var signIn = AsSession(await NewSessionAsync(user.Id));
        var service = NewExternalAuth(identity, signIn);
        var proof = (await signIn.CreateReauthProofAsync(user.Id)).Proof;

        var begin = await service.BeginAsync(ExternalAuthProviders.Google, new ExternalAuthBeginRequest
        {
            Intent = ExternalAuthIntent.Link,
            Client = ExternalAuthClient.Android,
            VerifierHash = AuthTicketStore.HashVerifier(AuthTicketStore.NewId()),
            ReauthProof = proof,
        }, user.Id, NewHttpContext().Request);
        Assert.True(begin.Success, begin.Message);
        Assert.Null(begin.Data.BrowserBindingSecret);

        await service.HandleCallbackAsync(ExternalAuthProviders.Google, "code", begin.Data.Response.FlowId, null, NewHttpContext());
        Assert.False(await _db.Credentials.AnyAsync(x => x.Identifier == identity.ProviderUserId));
    }

    [Fact]
    public async Task ExternalReauth_GivesAProofOnlyToTheSessionThatStartedIt()
    {
        var identity = NewGoogleIdentity();
        var user = await RegisterExternalUserAsync(identity);
        var session = await NewSessionAsync(user.Id);
        var service = NewExternalAuth(identity, AsSession(session));

        var (reauth, verifier) = await RunExternalFlowAsync(service, ExternalAuthIntent.Reauth, user.Id);
        Assert.Equal(ExternalAuthResults.Reauth, reauth.Result);

        // Another user's session can't turn the ticket into a proof.
        var (other, _) = await RegisterPasswordUserAsync();
        var otherSession = await NewSessionAsync(other.Id);
        var request = new ReauthRequest { ExternalTicket = reauth.Ticket, ExternalVerifier = verifier };
        Assert.False((await PostAsSessionAsync(otherSession, "api/users/me/reauth", request)).IsSuccessStatusCode);

        var response = await PostAsSessionAsync(session, "api/users/me/reauth", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var proof = await response.Content.ReadFromJsonAsync<ReauthResponse>();
        Assert.False(string.IsNullOrEmpty(proof?.Proof));

        // Each ticket gives one proof.
        Assert.False((await PostAsSessionAsync(session, "api/users/me/reauth", request)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExternalSignIn_TicketStopsWorkingOnceTheAccountIsUnlinked()
    {
        var identity = NewGoogleIdentity();
        var user = await RegisterExternalUserAsync(identity);
        var (result, verifier) = await RunExternalFlowAsync(NewExternalAuth(identity), ExternalAuthIntent.Login);
        Assert.Equal(ExternalAuthResults.Login, result.Result);

        _db.Credentials.Add(new Valour.Database.Credential
        {
            Id = Valour.Server.Database.IdManager.Generate(),
            UserId = user.Id,
            CredentialType = CredentialType.DISCORD,
            Identifier = "d-" + RandomName(),
            DisplayName = "@tester",
        });
        await _db.SaveChangesAsync();
        var google = await _db.Credentials.FirstAsync(x => x.Identifier == identity.ProviderUserId);
        Assert.True((await _signInMethods.RemoveMethodAsync(user.Id, google.Id)).Success);

        var signIn = await PostTokenAsync(new TokenRequest { ExternalTicket = result.Ticket, ExternalVerifier = verifier });
        Assert.False(signIn.Success);
    }

    [Fact]
    public async Task DeviceKey_NeedsTheAuthenticatorCodeWhenTwoFactorIsOn()
    {
        var (user, _) = await RegisterPasswordUserAsync();
        var session = await NewSessionAsync(user.Id);
        var proof = (await AsSession(session).CreateReauthProofAsync(user.Id)).Proof;

        _db.MultiAuths.Add(new Valour.Database.MultiAuth
        {
            Id = Valour.Server.Database.IdManager.Generate(),
            UserId = user.Id,
            Type = "app",
            Secret = "unused",
            Verified = true,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new DeviceKeyRegisterRequest
        {
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            DeviceName = "Test phone",
            ReauthProof = proof,
        };

        var withoutCode = await PostAsSessionAsync(session, "api/auth/device/register", request);
        Assert.False(withoutCode.IsSuccessStatusCode);
        Assert.Contains("authenticator code", await withoutCode.Content.ReadAsStringAsync());

        request.MultiFactorCode = "000000";
        Assert.False((await PostAsSessionAsync(session, "api/auth/device/register", request)).IsSuccessStatusCode);
        Assert.False(await _db.Credentials.AnyAsync(x => x.UserId == user.Id && x.CredentialType == CredentialType.DEVICE_KEY));
    }
}
