using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

/// <summary>
/// Exercises the federation node-to-hub trust path directly: a node signs an
/// S2S token with its own key, the hub authenticates it against the stored
/// public key, and only then may the node write its planet stubs.
/// </summary>
[Collection("ApiCollection")]
public class FederationServiceTests : IAsyncLifetime
{
    private const string NodeDomain = "testnode.example.com";

    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly FederationKeyService _keyService;
    private readonly FederationHubService _hubService;
    private readonly FederationPlanetRegistryService _registry;
    private readonly FederationMigrationService _migrationService;
    private readonly PlanetSnapshotService _snapshotService;

    private FederationConfig _previousConfig;
    private string _nodeJwk = null!;

    private sealed class PullBackFailureHandler : HttpMessageHandler
    {
        public List<string> ExportGrants { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/export", StringComparison.Ordinal) == true &&
                request.Headers.TryGetValues("X-Valour-Migration-Grant", out var grants))
            {
                ExportGrants.Add(grants.Single());
            }

            // Simulate both the failed export and a lost/failed abort request.
            // The hub must retain the pending grant id so its next retry can
            // resume the node's already-frozen migration.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StaticJsonHandler(object document) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(document),
            });
    }

    private sealed class HubMetadataHandler(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == ValourFederation.HubWellKnownRoute)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jwks, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    hosts = new { rootDomain = HostingConfig.Current.RootDomain },
                }),
            });
        }
    }

    /// <summary>
    /// Simulates a node with a recently cached hub key set while the hub's
    /// reconciliation endpoint is unavailable. The invitation must still be
    /// cryptographically redeemable exactly once by its intended recipient.
    /// </summary>
    private sealed class OfflineInviteHandler(
        string jwks,
        HttpStatusCode reconciliationStatus = HttpStatusCode.ServiceUnavailable) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == ValourFederation.HubWellKnownRoute)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jwks, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            if (request.RequestUri?.AbsolutePath == "/.well-known/valour-instance")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { hosts = new { rootDomain = HostingConfig.Current.RootDomain } }),
                });
            }

            return Task.FromResult(new HttpResponseMessage(reconciliationStatus));
        }
    }

    public FederationServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _keyService = _scope.ServiceProvider.GetRequiredService<FederationKeyService>();
        _hubService = _scope.ServiceProvider.GetRequiredService<FederationHubService>();
        _registry = _scope.ServiceProvider.GetRequiredService<FederationPlanetRegistryService>();
        _migrationService = _scope.ServiceProvider.GetRequiredService<FederationMigrationService>();
        _snapshotService = _scope.ServiceProvider.GetRequiredService<PlanetSnapshotService>();
    }

    public async ValueTask InitializeAsync()
    {
        _previousConfig = FederationConfig.Current;

        // Act as both hub and node so both key purposes exist.
        _ = new FederationConfig
        {
            HubEnabled = true,
            HubUrl = "https://" + HostingConfig.Current.RootDomain,
            NodeDomain = NodeDomain,
            AllowInsecure = true,
        };

        await _keyService.EnsureKeysAsync();
        _nodeJwk = await _keyService.GetNodePublicJwkAsync();

        // Register + activate the node with its published key (as the hub would
        // after a successful challenge).
        var existing = await _db.FederatedNodes.FindAsync(NodeDomain);
        if (existing is not null)
            _db.FederatedNodes.Remove(existing);
        await _db.SaveChangesAsync();

        await _db.FederatedNodes.AddAsync(new Valour.Database.FederatedNode
        {
            Domain = NodeDomain,
            OwnerId = ISharedUser.VictorUserId,
            NodePublicJwk = _nodeJwk,
            Status = Valour.Database.FederatedNodeStatus.Active,
            CreatedAt = DateTime.UtcNow,
            VerifiedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var existing = await _db.FederatedNodes.FindAsync(NodeDomain);
        if (existing is not null)
        {
            _db.FederatedPlanetStubs.RemoveRange(
                _db.FederatedPlanetStubs.Where(x => x.NodeDomain == NodeDomain || x.NodeDomain == "attacker-node.example.com"));
            _db.FederatedPurges.RemoveRange(
                _db.FederatedPurges.Where(x => x.NodeDomain == NodeDomain || x.NodeDomain == "attacker-node.example.com"));
            _db.FederatedNodes.Remove(existing);
            await _db.SaveChangesAsync();
        }

        // Restore whatever federation config was in place before this test.
        FederationConfig.Current = _previousConfig;
        _scope.Dispose();
    }

    private async Task<string> MintNodeTokenAsync(string audience, int? protocol = ValourFederation.ProtocolVersion)
    {
        var creds = await _keyService.GetNodeSigningCredentialsAsync();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = NodeDomain,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", NodeDomain),
            }),
            Expires = DateTime.UtcNow.AddMinutes(5),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = creds,
            Claims = protocol.HasValue
                ? new Dictionary<string, object> { ["protocol"] = protocol.Value }
                : null,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task ValidNodeToken_Authenticates_AndCanReserveAndUpsertStub()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);

        var domain = await _hubService.AuthenticateNodeAsync(token);
        Assert.Equal(NodeDomain, domain);

        var reserve = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = "Federated Planet",
            OwnerId = ISharedUser.VictorUserId,
            Discoverable = true,
        });
        Assert.True(reserve.Success, reserve.Message);
        Assert.True(reserve.Data!.Id > 0);

        var upsert = await _registry.UpsertAsync(domain!, reserve.Data.Id, new FederatedPlanetStubRequest
        {
            Name = "Renamed Federated Planet",
            OwnerId = ISharedUser.VictorUserId,
            MemberCount = 42,
            Discoverable = false,
        });
        Assert.True(upsert.Success, upsert.Message);

        var stored = await _db.FederatedPlanetStubs.FindAsync(reserve.Data.Id);
        Assert.NotNull(stored);
        Assert.Equal("Renamed Federated Planet", stored!.Name);
        Assert.Equal(ISharedUser.VictorUserId, stored.OwnerId);
        Assert.Equal(42, stored.MemberCount);
        Assert.False(stored.Discoverable);
    }

    [Fact]
    public async Task HubOnlyRegistryAndMigrationServices_RejectCallsOnACommunityNode()
    {
        var previousHubEnabled = FederationConfig.Current.HubEnabled;
        FederationConfig.Current.HubEnabled = false;
        try
        {
            var reserve = await _registry.ReserveAsync(NodeDomain, new FederatedPlanetStubRequest
            {
                Name = "Must not register on a node",
                OwnerId = ISharedUser.VictorUserId,
            });
            var abort = await _migrationService.AbortAsync(ISharedUser.VictorUserId, IdManager.Generate());

            Assert.False(reserve.Success);
            Assert.False(abort.Success);
            Assert.Contains("federation hub", reserve.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("federation hub", abort.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            FederationConfig.Current.HubEnabled = previousHubEnabled;
        }
    }

    [Fact]
    public async Task WrongAudienceToken_IsRejected()
    {
        var token = await MintNodeTokenAsync("some-other-hub.example.com");
        var domain = await _hubService.AuthenticateNodeAsync(token);
        Assert.Null(domain);
    }

    [Fact]
    public async Task LegacyNodeS2STokenWithoutProtocol_IsRejected()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain, protocol: null);

        Assert.Null(await _hubService.AuthenticateNodeAsync(token));
    }

    [Fact]
    public async Task NodeS2SToken_UsesCurrentProtocol()
    {
        var nodeService = new FederationNodeService(
            _db,
            _scope.ServiceProvider.GetRequiredService<UserService>(),
            _scope.ServiceProvider.GetRequiredService<PlanetMemberService>(),
            _scope.ServiceProvider.GetRequiredService<TokenService>(),
            new TestHttpClientFactory(new HubMetadataHandler(await _keyService.GetJwksJsonAsync())),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());

        var token = await nodeService.MintS2STokenAsync(_keyService);

        Assert.False(string.IsNullOrWhiteSpace(token));
        var protocol = new JsonWebTokenHandler().ReadJsonWebToken(token).Claims
            .Single(x => x.Type == "protocol").Value;
        Assert.Equal(ValourFederation.ProtocolVersion.ToString(), protocol);
        Assert.Equal(NodeDomain, await _hubService.AuthenticateNodeAsync(token));
    }

    [Fact]
    public async Task NodeVerification_RejectsWellKnownDocumentsThatOmitProtocolVersion()
    {
        const string legacyDomain = "legacy-protocol.example.com";
        var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, legacyDomain);
        Assert.True(registration.Success, registration.Message);

        try
        {
            // Deliberately omit protocolVersion. An old node must not be
            // mistaken for v2 merely because the client model has a default.
            var strictHub = new FederationHubService(
                _db,
                _keyService,
                new TestHttpClientFactory(new StaticJsonHandler(new
                {
                    domain = legacyDomain,
                    challenge = registration.Data!.Challenge,
                    version = "legacy",
                    workerId = 43,
                    publicJwk = _nodeJwk,
                })),
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationHubService>>());

            var verification = await strictHub.VerifyNodeAsync(ISharedUser.VictorUserId, legacyDomain);

            Assert.False(verification.Success);
            Assert.Contains("protocol", verification.Message, StringComparison.OrdinalIgnoreCase);
            var stored = await _db.FederatedNodes.FindAsync(legacyDomain);
            Assert.Equal(Valour.Database.FederatedNodeStatus.PendingVerification, stored!.Status);
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == legacyDomain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task TokenMinting_DoesNotDependOnCommunityNodeWorkerIds()
    {
        var strictHub = new FederationHubService(
            _db,
            _keyService,
            new TestHttpClientFactory(new StaticJsonHandler(new
            {
                domain = NodeDomain,
                challenge = "not-needed-after-verification",
                version = "test",
                protocolVersion = ValourFederation.ProtocolVersion,
                publicJwk = _nodeJwk,
            })),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationHubService>>());
        var user = await _scope.ServiceProvider.GetRequiredService<UserService>().GetAsync(ISharedUser.VictorUserId);

        var mint = await strictHub.MintTokenAsync(user, NodeDomain);

        // The fixture user has not joined this node, so minting must still
        // fail on membership. Crucially, the live descriptor no longer fails
        // a legacy descriptor-field consistency check before that authorization step.
        Assert.False(mint.Success);
        Assert.DoesNotContain("verification", mint.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MigrationOwner_CanMintDestinationSessionBeforeFirstMembershipExists()
    {
        var owner = await _scope.ServiceProvider.GetRequiredService<UserService>()
            .GetAsync(_fixture.Client.Me.Id);
        Assert.NotNull(owner);

        var planetId = IdManager.Generate();
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = owner!.Id,
            Name = "Migration session test",
            Description = "Owner needs a destination session before import.",
            LockedForMigration = true,
        });
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = planetId,
            TargetDomain = NodeDomain,
            Status = Valour.Database.FederatedMigrationStatus.Pending,
            GrantId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        try
        {
            var verifiedHub = new FederationHubService(
                _db,
                _keyService,
                new TestHttpClientFactory(new StaticJsonHandler(new
                {
                    domain = NodeDomain,
                    challenge = "not-needed-after-verification",
                    version = "test",
                    protocolVersion = ValourFederation.ProtocolVersion,
                    workerId = 42,
                    publicJwk = _nodeJwk,
                })),
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationHubService>>());

            // There is intentionally no FederatedMembership yet: the import
            // itself creates the destination copy that will receive it.
            Assert.False(await _db.FederatedMemberships.AnyAsync(x =>
                x.UserId == owner.Id && x.NodeDomain == NodeDomain));

            var minted = await verifiedHub.MintTokenAsync(owner, NodeDomain);

            Assert.True(minted.Success, minted.Message);
            var token = new JsonWebTokenHandler().ReadJsonWebToken(minted.Data!.Token);
            Assert.Equal(NodeDomain, token.Audiences.Single());
            Assert.Equal(owner.Id.ToString(), token.Subject);
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.FederatedAcceptedDomains
                .Where(x => x.UserId == owner.Id && x.Domain == NodeDomain)
                .ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task NodeVerification_RejectsMalformedPublicSigningKey()
    {
        const string invalidKeyDomain = "invalid-node-key.example.com";
        var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, invalidKeyDomain);
        Assert.True(registration.Success, registration.Message);

        try
        {
            var strictHub = new FederationHubService(
                _db,
                _keyService,
                new TestHttpClientFactory(new StaticJsonHandler(new
                {
                    domain = invalidKeyDomain,
                    challenge = registration.Data!.Challenge,
                    version = "test",
                    protocolVersion = ValourFederation.ProtocolVersion,
                    workerId = 44,
                    publicJwk = "{}",
                })),
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationHubService>>());

            var verification = await strictHub.VerifyNodeAsync(ISharedUser.VictorUserId, invalidKeyDomain);

            Assert.False(verification.Success);
            Assert.Contains("P-256", verification.Message, StringComparison.OrdinalIgnoreCase);
            var stored = await _db.FederatedNodes.FindAsync(invalidKeyDomain);
            Assert.Equal(Valour.Database.FederatedNodeStatus.PendingVerification, stored!.Status);
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == invalidKeyDomain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task HubCredentialValidation_RejectsTokensWithoutCurrentProtocol()
    {
        var nodeService = new FederationNodeService(
            _db,
            _scope.ServiceProvider.GetRequiredService<UserService>(),
            _scope.ServiceProvider.GetRequiredService<PlanetMemberService>(),
            _scope.ServiceProvider.GetRequiredService<TokenService>(),
            new TestHttpClientFactory(new HubMetadataHandler(await _keyService.GetJwksJsonAsync())),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());
        var credentials = await _keyService.GetHubSigningCredentialsAsync();

        string Mint(IDictionary<string, object> claims) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = HostingConfig.Current.RootDomain,
            Audience = NodeDomain,
            Expires = DateTime.UtcNow.AddMinutes(5),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = credentials,
            Claims = claims,
        });

        var missingProtocol = Mint(new Dictionary<string, object> { ["sub"] = ISharedUser.VictorUserId.ToString() });
        var obsoleteProtocol = Mint(new Dictionary<string, object>
        {
            ["sub"] = ISharedUser.VictorUserId.ToString(),
            ["protocol"] = ValourFederation.ProtocolVersion - 1,
        });
        var currentProtocol = Mint(new Dictionary<string, object>
        {
            ["sub"] = ISharedUser.VictorUserId.ToString(),
            ["protocol"] = ValourFederation.ProtocolVersion,
        });

        Assert.Null(await nodeService.ValidateHubSignedTokenAsync(missingProtocol, NodeDomain));
        Assert.Null(await nodeService.ValidateHubSignedTokenAsync(obsoleteProtocol, NodeDomain));
        Assert.NotNull(await nodeService.ValidateHubSignedTokenAsync(currentProtocol, NodeDomain));
    }

    [Fact]
    public async Task ExchangedNodeSession_ExpiresNoLaterThanItsHubCredential()
    {
        var hubUserId = IdManager.Generate();
        var nodeService = new FederationNodeService(
            _db,
            _scope.ServiceProvider.GetRequiredService<UserService>(),
            _scope.ServiceProvider.GetRequiredService<PlanetMemberService>(),
            _scope.ServiceProvider.GetRequiredService<TokenService>(),
            new TestHttpClientFactory(new HubMetadataHandler(await _keyService.GetJwksJsonAsync())),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());
        var credentials = await _keyService.GetHubSigningCredentialsAsync();
        var issuedAt = DateTime.UtcNow;
        var hubToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = HostingConfig.Current.RootDomain,
            Audience = NodeDomain,
            Expires = issuedAt.AddMinutes(15),
            IssuedAt = issuedAt,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = hubUserId.ToString(),
                ["name"] = "Session Test",
                ["subscription"] = string.Empty,
                ["protocol"] = ValourFederation.ProtocolVersion,
                ["memberships"] = Array.Empty<string>(),
            },
        });

        try
        {
            var exchange = await nodeService.ExchangeAsync(hubToken, "127.0.0.1");

            Assert.True(exchange.Success, exchange.Message);
            Assert.InRange(exchange.Data!.TimeExpires,
                issuedAt.AddMinutes(14), issuedAt.AddMinutes(15).AddSeconds(2));
        }
        finally
        {
            await _db.AuthTokens.Where(x => x.UserId == hubUserId && x.AppId == "FEDERATION").ExecuteDeleteAsync();
            await _db.UserProfiles.Where(x => x.Id == hubUserId).ExecuteDeleteAsync();
            await _db.Users.Where(x => x.Id == hubUserId).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task ForwardMigrationGrant_UsesCurrentProtocol()
    {
        var planetId = IdManager.Generate();
        var ownerHadAcceptedNode = await _db.FederatedAcceptedDomains.AnyAsync(x =>
            x.UserId == ISharedUser.VictorUserId && x.Domain == NodeDomain);
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = ISharedUser.VictorUserId,
            Name = "Protocol marker migration test",
            Description = "A migration grant must be accepted by current nodes.",
        });
        await _db.SaveChangesAsync();

        try
        {
            var initiated = await _migrationService.InitiateAsync(ISharedUser.VictorUserId, planetId, NodeDomain);

            Assert.True(initiated.Success, initiated.Message);
            var token = new JsonWebTokenHandler().ReadJsonWebToken(initiated.Data!.Grant);
            var protocol = token.Claims.Single(x => x.Type == "protocol").Value;
            Assert.Equal(ValourFederation.ProtocolVersion.ToString(), protocol);
            Assert.True(await _db.FederatedAcceptedDomains.AnyAsync(x =>
                x.UserId == ISharedUser.VictorUserId && x.Domain == NodeDomain));
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
            if (!ownerHadAcceptedNode)
            {
                await _db.FederatedAcceptedDomains
                    .Where(x => x.UserId == ISharedUser.VictorUserId && x.Domain == NodeDomain)
                    .ExecuteDeleteAsync();
            }
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task OfflineRecipientBoundInvite_RedeemsOnceWithProof_AndCreatesARecoverableReceipt()
    {
        var userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        var planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        var memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var tokenService = _scope.ServiceProvider.GetRequiredService<TokenService>();
        var loggerFactory = _scope.ServiceProvider.GetRequiredService<ILogger<FederationInviteService>>();
        var owner = await userService.GetAsync(_fixture.Client.Me.Id);
        Assert.NotNull(owner);

        var recipientDetails = await _fixture.RegisterUser();
        var recipientRow = await _db.Users.FirstAsync(x => x.Name == recipientDetails.Username);
        var recipientId = recipientRow.Id;
        var recipient = await userService.GetAsync(recipientId);
        Assert.NotNull(recipient);
        long planetId = 0;

        try
        {
            var planet = await planetService.CreateAsync(new Valour.Server.Models.Planet
            {
                Name = "Offline invite redemption test",
                Description = "Verifies offline proof-bound federation invite redemption.",
                OwnerId = owner!.Id,
            }, owner);
            Assert.True(planet.Success, planet.Message);
            planetId = planet.Data!.Id;

            await _db.FederatedPlanetStubs.AddAsync(new Valour.Database.FederatedPlanetStub
            {
                Id = planetId,
                NodeDomain = NodeDomain,
                Name = planet.Data.Name,
                Description = planet.Data.Description,
                OwnerId = owner.Id,
                Public = false,
                Discoverable = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();

            var hubInvites = _scope.ServiceProvider.GetRequiredService<FederationInviteService>();
            var created = await hubInvites.CreateAsync(owner.Id, new FederatedInviteGrantCreateRequest
            {
                PlanetId = planetId,
                IntendedUserId = recipientId,
                MaxUses = 1,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            Assert.True(created.Success, created.Message);

            using var proofKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicParameters = proofKey.ExportParameters(false);
            var publicJwk = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64Url(publicParameters.Q.X!),
                ["y"] = Base64Url(publicParameters.Q.Y!),
            });
            var passport = await hubInvites.MintPassportAsync(recipient, publicJwk);
            Assert.True(passport.Success, passport.Message);

            // This test uses one database for both roles. Present the recipient
            // as the destination's shadow row, as a real separate node would.
            recipientRow.IsFederated = true;
            await _db.SaveChangesAsync();

            var grantId = new JsonWebTokenHandler().ReadJsonWebToken(created.Data!.Grant).Id;
            var passportId = new JsonWebTokenHandler().ReadJsonWebToken(passport.Data!.Token).Id;
            var proof = proofKey.SignData(
                System.Text.Encoding.UTF8.GetBytes(
                    ValourFederation.BuildInviteProofPayload(
                        grantId, NodeDomain, planetId, recipientId, 1, passportId, recipientId)),
                HashAlgorithmName.SHA256);
            var request = new FederatedInviteRedeemRequest
            {
                Grant = created.Data.Grant,
                Passport = passport.Data.Token,
                Proof = Base64Url(proof),
            };

            var offlineFactory = new TestHttpClientFactory(new OfflineInviteHandler(await _keyService.GetJwksJsonAsync()));
            var nodeService = new FederationNodeService(
                _db, userService, memberService, tokenService, offlineFactory,
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());
            var nodeInvites = new FederationInviteService(
                _db, _keyService, nodeService, offlineFactory, loggerFactory);

            var redeemed = await nodeInvites.RedeemOnNodeAsync(request, "127.0.0.1");
            Assert.True(redeemed.Success, redeemed.Message);
            Assert.NotNull(redeemed.Data);
            Assert.True(await _db.PlanetMembers.AnyAsync(x => x.PlanetId == planetId && x.UserId == recipientId));

            var receipt = await _db.FederatedInviteRedemptions.FindAsync(grantId, recipientId);
            Assert.NotNull(receipt);
            Assert.Null(receipt!.ReportedAt);
            Assert.False(string.IsNullOrWhiteSpace(receipt.Passport));
            Assert.False(string.IsNullOrWhiteSpace(receipt.Proof));

            // Replaying the same proof is idempotent for the intended user and
            // must not consume a second use or create a second redemption.
            var replay = await nodeInvites.RedeemOnNodeAsync(request, "127.0.0.1");
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(1, await _db.FederatedInviteRedemptions
                .CountAsync(x => x.GrantId == grantId && x.UserId == recipientId));
            Assert.Equal(1, (await _db.FederatedInviteGrants.FindAsync(grantId))!.Uses);

            // A proof made from a modified copy of the grant must not work for
            // the real grant. This prevents a phishing host from changing the
            // destination claim, collecting a recipient proof, then replaying
            // that proof alongside the original signed capability.
            var alteredScopeProof = proofKey.SignData(
                System.Text.Encoding.UTF8.GetBytes(
                    ValourFederation.BuildInviteProofPayload(
                        grantId, "attacker-node.example.com", planetId, recipientId, 1, passportId, recipientId)),
                HashAlgorithmName.SHA256);
            var alteredScope = await nodeInvites.RedeemOnNodeAsync(new FederatedInviteRedeemRequest
            {
                Grant = created.Data.Grant,
                Passport = passport.Data.Token,
                Proof = Base64Url(alteredScopeProof),
            }, "127.0.0.1");
            Assert.False(alteredScope.Success);

            // The hub deliberately responded, so even a rate limit is an
            // authoritative fail-closed result rather than permission to use
            // the offline cached-key window.
            var rateLimitedGrant = await hubInvites.CreateAsync(owner.Id, new FederatedInviteGrantCreateRequest
            {
                PlanetId = planetId,
                IntendedUserId = recipientId,
                MaxUses = 1,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            Assert.True(rateLimitedGrant.Success, rateLimitedGrant.Message);
            var rateLimitedGrantId = new JsonWebTokenHandler().ReadJsonWebToken(rateLimitedGrant.Data!.Grant).Id;
            var rateLimitedProof = proofKey.SignData(
                System.Text.Encoding.UTF8.GetBytes(
                    ValourFederation.BuildInviteProofPayload(
                        rateLimitedGrantId, NodeDomain, planetId, recipientId, 1, passportId, recipientId)),
                HashAlgorithmName.SHA256);
            var rateLimitedRequest = new FederatedInviteRedeemRequest
            {
                Grant = rateLimitedGrant.Data.Grant,
                Passport = passport.Data.Token,
                Proof = Base64Url(rateLimitedProof),
            };
            var rateLimitedFactory = new TestHttpClientFactory(new OfflineInviteHandler(
                await _keyService.GetJwksJsonAsync(), HttpStatusCode.TooManyRequests));
            var rateLimitedNodeService = new FederationNodeService(
                _db, userService, memberService, tokenService, rateLimitedFactory,
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());
            var rateLimitedNodeInvites = new FederationInviteService(
                _db, _keyService, rateLimitedNodeService, rateLimitedFactory, loggerFactory);

            var rateLimited = await rateLimitedNodeInvites.RedeemOnNodeAsync(rateLimitedRequest, "127.0.0.1");
            Assert.False(rateLimited.Success);
            Assert.Null(await _db.FederatedInviteRedemptions.FindAsync(rateLimitedGrantId, recipientId));
            Assert.Equal(0, (await _db.FederatedInviteGrants.FindAsync(rateLimitedGrantId))!.Uses);

            // Losing the local node signing key is not a hub outage. The node
            // must fail closed rather than use stale JWKS material it could no
            // longer reconcile with the hub.
            var nodeKey = await _db.FederationKeys.AsNoTracking()
                .FirstAsync(x => x.Purpose == FederationKeyService.NodePurpose && x.Active);
            try
            {
                await _db.FederationKeys
                    .Where(x => x.Id == nodeKey.Id)
                    .ExecuteDeleteAsync();
                FederationKeyService.InvalidateCaches();

                var missingKeyFactory = new TestHttpClientFactory(new OfflineInviteHandler(
                    await _keyService.GetJwksJsonAsync(), HttpStatusCode.ServiceUnavailable));
                var missingKeyNodeService = new FederationNodeService(
                    _db, userService, memberService, tokenService, missingKeyFactory,
                    _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());
                var missingKeyNodeInvites = new FederationInviteService(
                    _db, _keyService, missingKeyNodeService, missingKeyFactory, loggerFactory);

                var missingKey = await missingKeyNodeInvites.RedeemOnNodeAsync(rateLimitedRequest, "127.0.0.1");
                Assert.False(missingKey.Success);
                Assert.Null(await _db.FederatedInviteRedemptions.FindAsync(rateLimitedGrantId, recipientId));
                Assert.Equal(0, (await _db.FederatedInviteGrants.FindAsync(rateLimitedGrantId))!.Uses);
            }
            finally
            {
                await _db.FederationKeys.AddAsync(new Valour.Database.FederationKey
                {
                    Id = nodeKey.Id,
                    Purpose = nodeKey.Purpose,
                    Algorithm = nodeKey.Algorithm,
                    PublicJwk = nodeKey.PublicJwk,
                    PrivateKeyProtected = nodeKey.PrivateKeyProtected,
                    Active = nodeKey.Active,
                    CreatedAt = nodeKey.CreatedAt,
                });
                await _db.SaveChangesAsync();
                FederationKeyService.InvalidateCaches();
            }
        }
        finally
        {
            if (planetId != 0)
            {
                await _db.AuthTokens.Where(x => x.AppId == "FEDERATION" && x.UserId == recipientId).ExecuteDeleteAsync();
                await _db.FederatedInviteRedemptions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
                await _db.FederatedInviteGrants.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
                await _db.FederatedPlanetStubs.Where(x => x.Id == planetId).ExecuteDeleteAsync();
                await _snapshotService.DeletePlanetDataAsync(planetId);
            }

            var storedRecipient = await _db.Users.FindAsync(recipientId);
            if (storedRecipient is not null)
            {
                storedRecipient.IsFederated = false;
                await _db.SaveChangesAsync();
                var recipientModel = await userService.GetAsync(recipientId);
                if (recipientModel is not null)
                    await userService.HardDelete(recipientModel);
            }

            _db.ChangeTracker.Clear();
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task TokenSignedWithForeignKey_IsRejected()
    {
        // A token with the right claims but signed by a key the hub never stored.
        using var rogue = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var creds = new SigningCredentials(new ECDsaSecurityKey(rogue) { KeyId = "rogue" }, SecurityAlgorithms.EcdsaSha256);
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = NodeDomain,
            Audience = HostingConfig.Current.RootDomain,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = creds,
        });

        var domain = await _hubService.AuthenticateNodeAsync(forged);
        Assert.Null(domain);
    }

    [Fact]
    public async Task Adopt_OfArbitraryId_IsRejected()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        // A migration's stub is created by the migration protocol. Letting a
        // node adopt a new arbitrary id would reserve a future global id.
        var existingId = Valour.Server.Database.IdManager.Generate();
        var adopt = await _registry.AdoptAsync(domain!, existingId, new FederatedPlanetStubRequest
        {
            Name = "Migrated Planet",
            OwnerId = ISharedUser.VictorUserId,
        });

        Assert.False(adopt.Success);
    }

    [Fact]
    public async Task Upsert_CannotRewriteReservedOwner()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        var first = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = "First", OwnerId = ISharedUser.VictorUserId,
        });
        Assert.True(first.Success);

        var second = await _registry.UpsertAsync(domain!, first.Data!.Id, new FederatedPlanetStubRequest
        {
            Name = "Owner spoof attempt", OwnerId = ISharedUser.VictorUserId + 999,
        });
        Assert.True(second.Success, second.Message);
        var stored = await _db.FederatedPlanetStubs.FindAsync(first.Data.Id);
        Assert.Equal(ISharedUser.VictorUserId, stored!.OwnerId);
    }

    [Fact]
    public async Task Upsert_ByDifferentNode_IsRejected()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        var reserve = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = "Owned Planet",
            OwnerId = ISharedUser.VictorUserId,
        });
        Assert.True(reserve.Success);

        // Another node must not be able to overwrite this stub.
        var hijack = await _registry.UpsertAsync("attacker-node.example.com", reserve.Data!.Id,
            new FederatedPlanetStubRequest { Name = "Hijacked", OwnerId = ISharedUser.VictorUserId });
        Assert.False(hijack.Success);
    }

    [Fact]
    public async Task CompletedMigration_CannotBeReissuedToADifferentDestination()
    {
        var planetId = IdManager.Generate();
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = ISharedUser.VictorUserId,
            Name = "Completed migration safety test",
            Description = "Must not fork to a second node.",
            LockedForMigration = true,
        });
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = planetId,
            TargetDomain = NodeDomain,
            Status = Valour.Database.FederatedMigrationStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            GrantId = Guid.NewGuid().ToString("N"),
        });
        await _db.SaveChangesAsync();

        try
        {
            var reissue = await _migrationService.InitiateAsync(
                ISharedUser.VictorUserId, planetId, "second-node.example.com");

            Assert.False(reissue.Success);
            Assert.Contains("already completed", reissue.Message, StringComparison.OrdinalIgnoreCase);
            var migration = await _db.FederatedMigrations.FindAsync(planetId);
            Assert.Equal(NodeDomain, migration!.TargetDomain);
            Assert.Equal(Valour.Database.FederatedMigrationStatus.Completed, migration.Status);
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task PullBackRetry_AfterFailedAbort_ReusesThePendingGrant()
    {
        var planetId = IdManager.Generate();
        var grantId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await _db.FederatedPlanetStubs.AddAsync(new Valour.Database.FederatedPlanetStub
        {
            Id = planetId,
            NodeDomain = NodeDomain,
            Name = "Retry safety test",
            Description = "Exercises a lost pull-back abort response.",
            OwnerId = ISharedUser.VictorUserId,
            Public = true,
            Discoverable = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = planetId,
            TargetDomain = HostingConfig.Current.RootDomain,
            Status = Valour.Database.FederatedMigrationStatus.Pending,
            CreatedAt = now,
            GrantId = grantId,
        });
        await _db.SaveChangesAsync();

        try
        {
            var handler = new PullBackFailureHandler();
            var retryService = new FederationMigrationService(
                _db,
                _keyService,
                _hubService,
                _scope.ServiceProvider.GetRequiredService<FederationNodeService>(),
                _scope.ServiceProvider.GetRequiredService<FederationNodeClient>(),
                _snapshotService,
                _scope.ServiceProvider.GetRequiredService<HostedPlanetService>(),
                new TestHttpClientFactory(handler),
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationMigrationService>>());

            var first = await retryService.PullBackAsync(ISharedUser.VictorUserId, planetId);
            var second = await retryService.PullBackAsync(ISharedUser.VictorUserId, planetId);

            Assert.False(first.Success);
            Assert.False(second.Success);
            var migration = await _db.FederatedMigrations.FindAsync(planetId);
            Assert.NotNull(migration);
            Assert.Equal(Valour.Database.FederatedMigrationStatus.Pending, migration!.Status);
            Assert.Equal(grantId, migration.GrantId);
            Assert.Equal(2, handler.ExportGrants.Count);
            Assert.All(handler.ExportGrants, grant =>
                Assert.Equal(grantId, new JsonWebTokenHandler().ReadJsonWebToken(grant).Id));
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.FederatedPlanetStubs.Where(x => x.Id == planetId).ExecuteDeleteAsync();
            // ExecuteDelete deliberately bypasses the tracker. Detach the
            // removed rows so collection cleanup in DisposeAsync cannot try to
            // delete this stub a second time.
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task PullBack_RejectsPrivateNodeAddressBeforeCreatingMigrationState()
    {
        var planetId = IdManager.Generate();
        var previousInsecure = FederationConfig.Current.AllowInsecure;
        FederationConfig.Current.AllowInsecure = false;
        await _db.FederatedNodes.AddAsync(new Valour.Database.FederatedNode
        {
            Domain = "localhost",
            OwnerId = ISharedUser.VictorUserId,
            NodePublicJwk = _nodeJwk,
            Status = Valour.Database.FederatedNodeStatus.Active,
            CreatedAt = DateTime.UtcNow,
            VerifiedAt = DateTime.UtcNow,
        });
        await _db.FederatedPlanetStubs.AddAsync(new Valour.Database.FederatedPlanetStub
        {
            Id = planetId,
            NodeDomain = "localhost",
            Name = "Private address test",
            OwnerId = ISharedUser.VictorUserId,
            Public = true,
            Discoverable = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        try
        {
            var result = await _migrationService.PullBackAsync(ISharedUser.VictorUserId, planetId);

            Assert.False(result.Success);
            Assert.Contains("public address", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(await _db.FederatedMigrations.FindAsync(planetId));
        }
        finally
        {
            FederationConfig.Current.AllowInsecure = previousInsecure;
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.FederatedPlanetStubs.Where(x => x.Id == planetId).ExecuteDeleteAsync();
            await _db.FederatedNodes.Where(x => x.Domain == "localhost").ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task PullBackPreparation_PreservesNodeAuthorizedData_AndMarksImportedHistory()
    {
        var planetId = IdManager.Generate();
        var channelId = IdManager.Generate();
        var maliciousRoleId = IdManager.Generate();
        var maliciousMemberId = IdManager.Generate();
        var messageId = IdManager.Generate();
        var threadId = IdManager.Generate();
        var pageId = IdManager.Generate();
        var attackerId = IdManager.Generate();

        // The registered community-node owner is the trust authority for its
        // community's data. The hub keeps its own planet ownership record, but
        // preserves the node's roles, members, moderation state, and history.
        var snapshot = new PlanetSnapshot
        {
            SourceDomain = NodeDomain,
            Planet = new PlanetSnapshotPlanet
            {
                Id = planetId,
                OwnerId = attackerId,
                Name = "Returned Planet",
                Description = "Snapshot from an untrusted node",
                Public = true,
                Discoverable = true,
                SelfHostedMedia = true,
            },
            Channels = new List<PlanetSnapshotChannel>
            {
                new()
                {
                    Id = channelId,
                    PlanetId = planetId,
                    Name = "general",
                    Description = "Untrusted channel history",
                    ChannelType = ChannelTypeEnum.PlanetChat,
                    IsDefault = true,
                    LastUpdateTime = DateTime.UtcNow,
                },
            },
            Roles = new List<PlanetSnapshotRole>
            {
                new()
                {
                    Id = maliciousRoleId,
                    PlanetId = planetId,
                    FlagBitIndex = 17,
                    Name = "forged administrator",
                    IsAdmin = true,
                    Permissions = long.MaxValue,
                },
            },
            PermissionNodes = new List<PlanetSnapshotPermNode>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    PlanetId = planetId,
                    RoleId = maliciousRoleId,
                    TargetId = channelId,
                    TargetType = ChannelTypeEnum.PlanetChat,
                },
            },
            Members = new List<PlanetSnapshotMember>
            {
                new()
                {
                    Id = maliciousMemberId,
                    PlanetId = planetId,
                    UserId = attackerId,
                    Rf0 = 1L << 17,
                },
            },
            Messages = new List<PlanetSnapshotMessage>
            {
                new()
                {
                    Id = messageId,
                    PlanetId = planetId,
                    ChannelId = channelId,
                    AuthorUserId = attackerId,
                    AuthorMemberId = maliciousMemberId,
                    Content = "Forged as the owner",
                    TimeSent = DateTime.UtcNow,
                },
            },
            Attachments = new List<PlanetSnapshotAttachment>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    MessageId = messageId,
                    Type = MessageAttachmentType.File,
                    CdnBucketItemId = "node-local-cdn-item",
                    Location = "https://cdn.community-node.example.com/history.txt",
                    FileName = "history.txt",
                    MimeType = "text/plain",
                },
            },
            Reactions = new List<PlanetSnapshotReaction>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    MessageId = messageId,
                    AuthorUserId = attackerId,
                    AuthorMemberId = maliciousMemberId,
                    Emoji = "👎",
                    CreatedAt = DateTime.UtcNow,
                },
            },
            Threads = new List<PlanetSnapshotThread>
            {
                new()
                {
                    Id = threadId,
                    PlanetId = planetId,
                    AuthorUserId = attackerId,
                    AuthorMemberId = maliciousMemberId,
                    Title = "Forged thread",
                    Content = "Untrusted history",
                    TimeCreated = DateTime.UtcNow,
                },
            },
            ThreadComments = new List<PlanetSnapshotThreadComment>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    PlanetId = planetId,
                    ThreadId = threadId,
                    AuthorUserId = attackerId,
                    AuthorMemberId = maliciousMemberId,
                    Content = "Untrusted comment",
                    TimeCreated = DateTime.UtcNow,
                },
            },
            WikiPages = new List<PlanetSnapshotWikiPage>
            {
                new()
                {
                    Id = pageId,
                    PlanetId = planetId,
                    Slug = "forged-page",
                    Title = "Forged page",
                    Content = "Untrusted wiki",
                    CreatedByUserId = attackerId,
                    LastEditedByUserId = ISharedUser.VictorUserId,
                    TimeCreated = DateTime.UtcNow,
                },
            },
            WikiRevisions = new List<PlanetSnapshotWikiRevision>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    PlanetId = planetId,
                    PageId = pageId,
                    AuthorUserId = attackerId,
                    Title = "Forged page",
                    Content = "Untrusted revision",
                    TimeCreated = DateTime.UtcNow,
                },
            },
            Bans = new List<PlanetSnapshotBan>
            {
                new()
                {
                    Id = IdManager.Generate(),
                    PlanetId = planetId,
                    IssuerId = attackerId,
                    TargetId = ISharedUser.VictorUserId,
                    TimeCreated = DateTime.UtcNow,
                },
            },
            AutomodTriggers = new List<PlanetSnapshotAutomodTrigger>
            {
                new()
                {
                    Id = Guid.NewGuid(), PlanetId = planetId, MemberAddedBy = maliciousMemberId,
                    Name = "node moderation", TriggerWords = "x",
                },
            },
            Users = new List<PlanetSnapshotUser>
            {
                new() { Id = attackerId, Name = "Attacker", Tag = "0000" },
                new() { Id = ISharedUser.VictorUserId, Name = "Forged Victor", Tag = "9999" },
            },
        };

        var prepared = await _migrationService.PreparePulledBackSnapshotAsync(
            snapshot, planetId, ISharedUser.VictorUserId, NodeDomain);

        Assert.True(prepared.Success, prepared.Message);
        Assert.Equal(ISharedUser.VictorUserId, snapshot.Planet.OwnerId);
        Assert.True(snapshot.Planet.Public);
        Assert.True(snapshot.Planet.Discoverable);
        Assert.True(snapshot.Planet.SelfHostedMedia);
        Assert.Single(snapshot.PermissionNodes);
        Assert.Single(snapshot.Bans);
        Assert.Single(snapshot.AutomodTriggers);
        Assert.Single(snapshot.Attachments);
        Assert.Null(snapshot.Attachments[0].CdnBucketItemId);

        var member = Assert.Single(snapshot.Members);
        Assert.Equal(attackerId, member.UserId);
        Assert.Equal(1L << 17, member.Rf0);

        var role = Assert.Single(snapshot.Roles);
        Assert.False(role.IsDefault);
        Assert.True(role.IsAdmin);
        Assert.Equal(17, role.FlagBitIndex);

        var expectedImportSource = $"federation:{NodeDomain}";
        Assert.All(snapshot.Messages, x =>
        {
            Assert.Equal(expectedImportSource, x.ImportSource);
        });
        Assert.All(snapshot.Reactions, x =>
        {
            Assert.Equal(expectedImportSource, x.ImportSource);
        });
        Assert.All(snapshot.Threads, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.ThreadComments, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.WikiPages, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.WikiRevisions, x => Assert.Equal(expectedImportSource, x.ImportSource));

        try
        {
            var import = await _snapshotService.ImportAsync(snapshot);
            Assert.True(import.Success, import.Message);

            Assert.Equal(ISharedUser.VictorUserId,
                await _db.Planets.Where(x => x.Id == planetId).Select(x => x.OwnerId).SingleAsync());
            Assert.Single(await _db.PlanetMembers.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.PermissionsNodes.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.PlanetBans.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.AutomodTriggers.Where(x => x.PlanetId == planetId).ToListAsync());
            var attachment = Assert.Single(await _db.MessageAttachments.Where(x => x.MessageId == messageId).ToListAsync());
            Assert.Equal("https://cdn.community-node.example.com/history.txt", attachment.Location);
            Assert.Null(attachment.CdnBucketItemId);
            Assert.True(await _db.Users.Where(x => x.Id == attackerId).Select(x => x.IsFederated).SingleAsync());
            Assert.Equal(expectedImportSource,
                await _db.Messages.Where(x => x.Id == messageId).Select(x => x.ImportSource).SingleAsync());
        }
        finally
        {
            _db.ChangeTracker.Clear();
            await _snapshotService.DeletePlanetDataAsync(planetId);
            await _db.UserProfiles.Where(x => x.Id == attackerId).ExecuteDeleteAsync();
            await _db.Users.Where(x => x.Id == attackerId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Purges_AreScopedAndCursorPaginatedPerNode()
    {
        var firstId = Valour.Server.Database.IdManager.Generate();
        var secondId = Valour.Server.Database.IdManager.Generate();
        await _db.FederatedPurges.AddRangeAsync(
            new Valour.Database.FederatedPurge
            {
                Id = firstId, SubjectUserId = 101, NodeDomain = NodeDomain, CreatedAt = DateTime.UtcNow,
            },
            new Valour.Database.FederatedPurge
            {
                Id = secondId, SubjectUserId = 202, NodeDomain = "attacker-node.example.com", CreatedAt = DateTime.UtcNow,
            });
        await _db.SaveChangesAsync();

        var page = await _hubService.GetPurgedUserIdsAsync(NodeDomain, 0);
        Assert.Contains(101, page.UserIds);
        Assert.DoesNotContain(202, page.UserIds);
        Assert.Equal(firstId, page.NextCursor);

        var empty = await _hubService.GetPurgedUserIdsAsync(NodeDomain, page.NextCursor);
        Assert.Empty(empty.UserIds);
    }
}
