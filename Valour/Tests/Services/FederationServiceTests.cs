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

    /// <summary>
    /// A node's self-reported member count must never buy discovery placement:
    /// stubs rank by hub-recorded FederatedMemberships (which the hub writes
    /// before the node ever sees the user), so a lying node with a fantasy
    /// count sorts below a modest planet with real hub-verified joins.
    /// </summary>
    [Fact]
    public async Task Discovery_RanksStubsByHubVerifiedJoins_NotNodeReportedCount()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);
        Assert.Equal(NodeDomain, domain);

        var liar = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = "Definitely One Billion Users",
            OwnerId = ISharedUser.VictorUserId,
            MemberCount = int.MaxValue,
            Public = true,
            Discoverable = true,
        });
        Assert.True(liar.Success, liar.Message);

        var honest = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = "Small But Real",
            OwnerId = ISharedUser.VictorUserId,
            MemberCount = 3,
            Public = true,
            Discoverable = true,
        });
        Assert.True(honest.Success, honest.Message);

        // Hub-verified joins for the honest planet only. Enough of them to keep
        // it inside the top-30 window alongside seeded official planets.
        var memberships = Enumerable.Range(0, 40).Select(_ => new Valour.Database.FederatedMembership
        {
            UserId = IdManager.Generate(),
            PlanetId = honest.Data!.Id,
            NodeDomain = NodeDomain,
            JoinedAt = DateTime.UtcNow,
        }).ToList();
        await _db.FederatedMemberships.AddRangeAsync(memberships);
        await _db.SaveChangesAsync();

        try
        {
            var planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
            var discovery = await planetService.GetDiscoveryPlanetsAsync();

            var honestIndex = discovery.FindIndex(x => x.PlanetId == honest.Data!.Id);
            var liarIndex = discovery.FindIndex(x => x.PlanetId == liar.Data!.Id);

            Assert.True(honestIndex >= 0, "hub-verified stub missing from discovery");

            // The liar either ranks below the verified planet or fell out of the
            // top list entirely — both mean the reported count bought nothing.
            if (liarIndex >= 0)
                Assert.True(honestIndex < liarIndex,
                    $"node-reported count outranked hub-verified joins (honest at {honestIndex}, liar at {liarIndex})");

            // The displayed number is still the node's report — ranking is what
            // changed, not the (badged) display.
            Assert.Equal(3, discovery[honestIndex].MemberCount);
        }
        finally
        {
            _db.FederatedMemberships.RemoveRange(memberships);
            var stubs = await _db.FederatedPlanetStubs
                .Where(x => x.Id == liar.Data!.Id || x.Id == honest.Data!.Id)
                .ToListAsync();
            _db.FederatedPlanetStubs.RemoveRange(stubs);
            await _db.SaveChangesAsync();
        }
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
            Assert.Contains("only available on the official server", reserve.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("only available on the official server", abort.Message, StringComparison.OrdinalIgnoreCase);
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
                ["jti"] = Guid.NewGuid().ToString("N"),
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
    public async Task ForwardMigration_DefaultDenyRequiresNodeOwnerApproval()
    {
        var ownerId = _fixture.Client.Me.Id;
        var planetId = IdManager.Generate();
        var ownerHadAcceptedNode = await _db.FederatedAcceptedDomains.AnyAsync(x =>
            x.UserId == ownerId && x.Domain == NodeDomain);
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = ownerId,
            Name = "Approval-gated migration test",
            Description = "A foreign node must consent before it hosts this planet.",
        });
        await _db.SaveChangesAsync();

        try
        {
            var denied = await _migrationService.InitiateAsync(ownerId, planetId, NodeDomain);
            Assert.False(denied.Success);
            Assert.Contains("not approved", denied.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await _db.FederatedMigrations.AnyAsync(x => x.PlanetId == planetId));

            var approval = await _hubService.CreateMigrationHostingApprovalAsync(
                ISharedUser.VictorUserId,
                NodeDomain,
                new FederatedMigrationHostingApprovalRequest { OwnerId = ownerId, PlanetId = planetId });
            Assert.True(approval.Success, approval.Message);

            var allowed = await _migrationService.InitiateAsync(ownerId, planetId, NodeDomain);
            Assert.True(allowed.Success, allowed.Message);
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.FederatedMigrationHostingApprovals
                .Where(x => x.NodeDomain == NodeDomain && x.OwnerId == ownerId && x.PlanetId == planetId)
                .ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
            if (!ownerHadAcceptedNode)
            {
                await _db.FederatedAcceptedDomains
                    .Where(x => x.UserId == ownerId && x.Domain == NodeDomain)
                    .ExecuteDeleteAsync();
            }
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task ForwardMigration_NodePublicPolicyAcceptsAnyEligibleOwner()
    {
        var ownerId = _fixture.Client.Me.Id;
        var planetId = IdManager.Generate();
        var node = await _db.FederatedNodes.FindAsync(NodeDomain);
        Assert.NotNull(node);
        var previousPolicy = node!.AllowsPublicMigrations;
        var ownerHadAcceptedNode = await _db.FederatedAcceptedDomains.AnyAsync(x =>
            x.UserId == ownerId && x.Domain == NodeDomain);
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = ownerId,
            Name = "Open-hosting migration test",
            Description = "An explicitly open node accepts this planet.",
        });
        node.AllowsPublicMigrations = true;
        await _db.SaveChangesAsync();

        try
        {
            var result = await _migrationService.InitiateAsync(ownerId, planetId, NodeDomain);
            Assert.True(result.Success, result.Message);
        }
        finally
        {
            await _db.FederatedMigrations.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
            node.AllowsPublicMigrations = previousPolicy;
            if (!ownerHadAcceptedNode)
            {
                await _db.FederatedAcceptedDomains
                    .Where(x => x.UserId == ownerId && x.Domain == NodeDomain)
                    .ExecuteDeleteAsync();
            }
            await _db.SaveChangesAsync();
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
    public async Task PullBackPreparation_KeepsNodeContent_ButOnlyHubVouchedIdentities()
    {
        var planetId = IdManager.Generate();
        var channelId = IdManager.Generate();
        var maliciousRoleId = IdManager.Generate();
        var ownerMemberId = IdManager.Generate();
        var forgedVictorMemberId = IdManager.Generate();
        var maliciousMemberId = IdManager.Generate();
        var messageId = IdManager.Generate();
        var threadId = IdManager.Generate();
        var pageId = IdManager.Generate();
        var attackerId = IdManager.Generate();
        var ownerId = _fixture.Client.Me.Id;
        var victorName = await _db.Users.Where(x => x.Id == ISharedUser.VictorUserId).Select(x => x.Name).SingleAsync();

        // The registered community-node owner is the trust authority for its
        // community's content, but not for hub identities. The snapshot below
        // adds a member row for an account that never joined through the hub
        // (Victor), invents an account the hub does not have (attacker), and
        // inflates derived counters.
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
                new() { Id = ownerMemberId, PlanetId = planetId, UserId = ownerId },
                new() { Id = forgedVictorMemberId, PlanetId = planetId, UserId = ISharedUser.VictorUserId, Rf0 = 1L << 17 },
                new() { Id = maliciousMemberId, PlanetId = planetId, UserId = attackerId, Rf0 = 1L << 17 },
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
                    BoostCount = 999,
                    CommentCount = 999,
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
                    BoostCount = 999,
                    ReplyCount = 999,
                },
            },
            ThreadBoosts = new List<PlanetSnapshotThreadBoost>
            {
                new()
                {
                    Id = IdManager.Generate(), PlanetId = planetId, ThreadId = threadId,
                    UserId = attackerId, CreatedAt = DateTime.UtcNow,
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
                new() { Id = ownerId, Name = "Forged Owner", Tag = "0000" },
                new() { Id = attackerId, Name = "Attacker", Tag = "0000" },
                new() { Id = ISharedUser.VictorUserId, Name = "Forged Victor", Tag = "9999" },
            },
        };

        var prepared = await _migrationService.PreparePulledBackSnapshotAsync(
            snapshot, planetId, ownerId, NodeDomain);

        Assert.True(prepared.Success, prepared.Message);
        Assert.Equal(ownerId, snapshot.Planet.OwnerId);
        Assert.True(snapshot.Planet.Public);
        Assert.True(snapshot.Planet.Discoverable);
        Assert.True(snapshot.Planet.SelfHostedMedia);
        Assert.Single(snapshot.PermissionNodes);
        Assert.Single(snapshot.Attachments);
        Assert.Null(snapshot.Attachments[0].CdnBucketItemId);

        // Roles are community content and survive; memberships are hub
        // identity and survive only for the owner.
        var role = Assert.Single(snapshot.Roles);
        Assert.True(role.IsAdmin);
        var member = Assert.Single(snapshot.Members);
        Assert.Equal(ownerId, member.UserId);

        // Content by an account the hub does not have is attributed to the
        // system account; per-account state for it is dropped.
        var message = Assert.Single(snapshot.Messages);
        Assert.Equal(ISharedUser.VictorUserId, message.AuthorUserId);
        Assert.Null(message.AuthorMemberId);
        Assert.Empty(snapshot.Reactions);
        Assert.Empty(snapshot.ThreadBoosts);
        Assert.Equal(ISharedUser.VictorUserId, Assert.Single(snapshot.Threads).AuthorUserId);
        Assert.Equal(ISharedUser.VictorUserId, Assert.Single(snapshot.ThreadComments).AuthorUserId);
        Assert.Equal(ISharedUser.VictorUserId, Assert.Single(snapshot.WikiPages).CreatedByUserId);
        Assert.Equal(ISharedUser.VictorUserId, Assert.Single(snapshot.WikiRevisions).AuthorUserId);
        Assert.Equal(ISharedUser.VictorUserId, Assert.Single(snapshot.Bans).IssuerId);

        // Derived counters come from the imported rows, not the node.
        Assert.Equal(0, snapshot.Threads[0].BoostCount);
        Assert.Equal(1, snapshot.Threads[0].CommentCount);
        Assert.Equal(0, snapshot.ThreadComments[0].BoostCount);
        Assert.Equal(0, snapshot.ThreadComments[0].ReplyCount);

        // An automod rule added by a removed member is kept under the owner.
        Assert.Equal(ownerMemberId, Assert.Single(snapshot.AutomodTriggers).MemberAddedBy);

        // Account descriptions come from the hub, never from the node.
        Assert.DoesNotContain(snapshot.Users, x => x.Id == attackerId);
        Assert.Equal(victorName, snapshot.Users.Single(x => x.Id == ISharedUser.VictorUserId).Name);

        var expectedImportSource = $"federation:{NodeDomain}";
        Assert.All(snapshot.Messages, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.Threads, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.ThreadComments, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.WikiPages, x => Assert.Equal(expectedImportSource, x.ImportSource));
        Assert.All(snapshot.WikiRevisions, x => Assert.Equal(expectedImportSource, x.ImportSource));

        try
        {
            var import = await _snapshotService.ImportAsync(snapshot, createMissingUsers: false);
            Assert.True(import.Success, import.Message);

            Assert.Equal(ownerId,
                await _db.Planets.Where(x => x.Id == planetId).Select(x => x.OwnerId).SingleAsync());
            var importedMember = Assert.Single(await _db.PlanetMembers.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Equal(ownerId, importedMember.UserId);
            Assert.Single(await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.PermissionsNodes.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.PlanetBans.Where(x => x.PlanetId == planetId).ToListAsync());
            Assert.Single(await _db.AutomodTriggers.Where(x => x.PlanetId == planetId).ToListAsync());
            // Cross-domain imports remap node-local message IDs. Verify the
            // attachment against the rewritten snapshot reference rather than
            // the source node's now-invalid local identifier.
            var importedMessageId = Assert.Single(snapshot.Messages).Id;
            var attachment = Assert.Single(await _db.MessageAttachments.Where(x => x.MessageId == importedMessageId).ToListAsync());
            Assert.Equal("https://cdn.community-node.example.com/history.txt", attachment.Location);
            Assert.Null(attachment.CdnBucketItemId);
            Assert.Equal(expectedImportSource,
                await _db.Messages.Where(x => x.Id == importedMessageId).Select(x => x.ImportSource).SingleAsync());

            // The node could neither create an account nor rename one.
            Assert.False(await _db.Users.AnyAsync(x => x.Id == attackerId));
            Assert.Equal(victorName,
                await _db.Users.Where(x => x.Id == ISharedUser.VictorUserId).Select(x => x.Name).SingleAsync());
        }
        finally
        {
            _db.ChangeTracker.Clear();
            await _snapshotService.DeletePlanetDataAsync(planetId);
        }
    }

    [Fact]
    public async Task PullBackPreparation_KeepsMembersWithHubRecordedMembership()
    {
        var planetId = IdManager.Generate();
        var channelId = IdManager.Generate();
        var memberId = IdManager.Generate();
        var memberUserId = _fixture.Client.Me.Id;

        await _db.FederatedMemberships.AddAsync(new Valour.Database.FederatedMembership
        {
            UserId = memberUserId,
            PlanetId = planetId,
            NodeDomain = NodeDomain,
            JoinedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var snapshot = new PlanetSnapshot
        {
            SourceDomain = NodeDomain,
            Planet = new PlanetSnapshotPlanet { Id = planetId, OwnerId = ISharedUser.VictorUserId, Name = "Members" },
            Channels = new List<PlanetSnapshotChannel>
            {
                new()
                {
                    Id = channelId, PlanetId = planetId, Name = "general",
                    ChannelType = ChannelTypeEnum.PlanetChat, IsDefault = true, LastUpdateTime = DateTime.UtcNow,
                },
            },
            Members = new List<PlanetSnapshotMember>
            {
                new() { Id = memberId, PlanetId = planetId, UserId = memberUserId },
            },
            Messages = new List<PlanetSnapshotMessage>
            {
                new()
                {
                    Id = IdManager.Generate(), PlanetId = planetId, ChannelId = channelId,
                    AuthorUserId = memberUserId, AuthorMemberId = memberId,
                    Content = "Real member history", TimeSent = DateTime.UtcNow,
                },
            },
            Users = new List<PlanetSnapshotUser>
            {
                new() { Id = memberUserId, Name = "Member", Tag = "0000" },
                new() { Id = ISharedUser.VictorUserId, Name = "Victor", Tag = "0000" },
            },
        };

        try
        {
            var prepared = await _migrationService.PreparePulledBackSnapshotAsync(
                snapshot, planetId, ISharedUser.VictorUserId, NodeDomain);

            Assert.True(prepared.Success, prepared.Message);
            Assert.Equal(memberUserId, Assert.Single(snapshot.Members).UserId);
            var message = Assert.Single(snapshot.Messages);
            Assert.Equal(memberUserId, message.AuthorUserId);
            Assert.Equal(memberId, message.AuthorMemberId);
        }
        finally
        {
            await _db.FederatedMemberships.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
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

    [Fact]
    public async Task NodeRegistration_UnverifiedDomainIsReservedOnlyUntilItExpires()
    {
        const string domain = "squat-test.example.com";
        var claimantId = _fixture.Client.Me.Id;

        try
        {
            var squat = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);
            Assert.True(squat.Success, squat.Message);

            // A fresh unverified registration still blocks other accounts.
            var blocked = await _hubService.RegisterNodeAsync(claimantId, domain);
            Assert.False(blocked.Success);
            Assert.Contains("unverified", blocked.Message, StringComparison.OrdinalIgnoreCase);

            // Re-registering during the window keeps the same challenge and
            // does not extend the reservation.
            var stored = await _db.FederatedNodes.FindAsync(domain);
            var reservedAt = stored!.CreatedAt;
            var again = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);
            Assert.True(again.Success, again.Message);
            Assert.Equal(squat.Data!.Challenge, again.Data!.Challenge);
            Assert.Equal(reservedAt, (await _db.FederatedNodes.FindAsync(domain))!.CreatedAt);

            // Once the window has passed, another account may claim it and
            // receives its own challenge.
            stored.CreatedAt = DateTime.UtcNow - FederationHubService.PendingRegistrationLifetime - TimeSpan.FromMinutes(1);
            await _db.SaveChangesAsync();

            var claimed = await _hubService.RegisterNodeAsync(claimantId, domain);
            Assert.True(claimed.Success, claimed.Message);
            Assert.NotEqual(squat.Data.Challenge, claimed.Data!.Challenge);
            _db.ChangeTracker.Clear();
            Assert.Equal(claimantId, (await _db.FederatedNodes.FindAsync(domain))!.OwnerId);
            Assert.Null(await _hubService.GetNodeStatusAsync(ISharedUser.VictorUserId, domain));
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == domain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task NodeRegistration_SuspendedNodeCannotReRegisterItself()
    {
        const string domain = "suspended-register.example.com";
        try
        {
            var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);
            Assert.True(registration.Success, registration.Message);
            Assert.True((await _hubService.SetNodeSuspendedAsync(domain, true)).Success);

            var again = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);

            Assert.False(again.Success);
            _db.ChangeTracker.Clear();
            Assert.Equal(Valour.Database.FederatedNodeStatus.Suspended, (await _db.FederatedNodes.FindAsync(domain))!.Status);
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == domain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task NodeRegistration_IsLimitedPerAccount()
    {
        var ownerId = _fixture.Client.Me.Id;
        var prefix = "cap-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Valour.Shared.TaskResult<FederatedNodeRegistrationResponse> last = default;
            for (var i = 0; i <= FederationHubService.MaxRegistrationsPerOwner; i++)
            {
                last = await _hubService.RegisterNodeAsync(ownerId, $"{prefix}-{i}.example.com");
                if (!last.Success)
                    break;
            }

            Assert.False(last.Success);
            Assert.Contains("at most", last.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(await _db.FederatedNodes.CountAsync(x => x.OwnerId == ownerId) <= FederationHubService.MaxRegistrationsPerOwner);
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain.StartsWith(prefix)).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task StaffNodeDeletion_RefusesNodesThatHostPlanets()
    {
        const string domain = "staff-delete.example.com";
        try
        {
            var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);
            Assert.True(registration.Success, registration.Message);
            Assert.True((await _hubService.DeleteNodeAsync(domain)).Success);
            _db.ChangeTracker.Clear();
            Assert.Null(await _db.FederatedNodes.FindAsync(domain));

            // The fixture node hosts a stub, so it must be suspended instead.
            var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
            var nodeDomain = await _hubService.AuthenticateNodeAsync(token);
            var reserve = await _registry.ReserveAsync(nodeDomain!, new FederatedPlanetStubRequest { Name = "Hosted" });
            Assert.True(reserve.Success, reserve.Message);

            var refused = await _hubService.DeleteNodeAsync(NodeDomain);
            Assert.False(refused.Success);
            Assert.NotNull(await _db.FederatedNodes.FindAsync(NodeDomain));
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == domain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task HostingApproval_RequiresVerifiedNode_AndDoesNotRevealPlanetOwnership()
    {
        const string pendingDomain = "pending-approval.example.com";
        var otherOwnerId = _fixture.Client.Me.Id;
        var planetId = IdManager.Generate();
        await _db.Planets.AddAsync(new Valour.Database.Planet
        {
            Id = planetId,
            OwnerId = ISharedUser.VictorUserId,
            Name = "Approval privacy",
            Description = "Owned by Victor, not by the approved account.",
        });
        await _db.SaveChangesAsync();

        try
        {
            var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, pendingDomain);
            Assert.True(registration.Success, registration.Message);

            var pending = await _hubService.CreateMigrationHostingApprovalAsync(
                ISharedUser.VictorUserId, pendingDomain,
                new FederatedMigrationHostingApprovalRequest { OwnerId = otherOwnerId, PlanetId = planetId });
            Assert.False(pending.Success);

            // The planet does not belong to otherOwnerId. The response matches
            // any other approval, and the stored approval never matches a
            // migration started by the planet's real owner.
            var mismatched = await _hubService.CreateMigrationHostingApprovalAsync(
                ISharedUser.VictorUserId, NodeDomain,
                new FederatedMigrationHostingApprovalRequest { OwnerId = otherOwnerId, PlanetId = planetId });
            Assert.True(mismatched.Success, mismatched.Message);

            var node = await _db.FederatedNodes.AsNoTracking().SingleAsync(x => x.Domain == NodeDomain);
            node.OwnerId = otherOwnerId;
            node.AllowsPublicMigrations = false;
            Assert.False(await _hubService.CanHostMigrationAsync(node, ISharedUser.VictorUserId, planetId));
        }
        finally
        {
            await _db.FederatedMigrationHostingApprovals
                .Where(x => x.NodeDomain == NodeDomain && x.PlanetId == planetId)
                .ExecuteDeleteAsync();
            await _db.FederatedNodes.Where(x => x.Domain == pendingDomain).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task NodeS2SToken_IsSingleUse_AndLifetimeIsBounded()
    {
        var nodeService = new FederationNodeService(
            _db,
            _scope.ServiceProvider.GetRequiredService<UserService>(),
            _scope.ServiceProvider.GetRequiredService<PlanetMemberService>(),
            _scope.ServiceProvider.GetRequiredService<TokenService>(),
            new TestHttpClientFactory(new HubMetadataHandler(await _keyService.GetJwksJsonAsync())),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>());

        var token = await nodeService.MintS2STokenAsync(_keyService);
        Assert.Equal(NodeDomain, await _hubService.AuthenticateNodeAsync(token));
        Assert.Null(await _hubService.AuthenticateNodeAsync(token));

        var creds = await _keyService.GetNodeSigningCredentialsAsync();
        var longLived = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = NodeDomain,
            Audience = HostingConfig.Current.RootDomain,
            Expires = DateTime.UtcNow.AddDays(30),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = creds,
            Claims = new Dictionary<string, object>
            {
                ["protocol"] = ValourFederation.ProtocolVersion,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
        });
        Assert.Null(await _hubService.AuthenticateNodeAsync(longLived));
    }

    [Fact]
    public async Task FederationExchange_AcceptsEachHubTokenOnce()
    {
        var hubUserId = IdManager.Generate();
        var nodeService = new FederationNodeService(
            _db,
            _scope.ServiceProvider.GetRequiredService<UserService>(),
            _scope.ServiceProvider.GetRequiredService<PlanetMemberService>(),
            _scope.ServiceProvider.GetRequiredService<TokenService>(),
            new TestHttpClientFactory(new HubMetadataHandler(await _keyService.GetJwksJsonAsync())),
            _scope.ServiceProvider.GetRequiredService<ILogger<FederationNodeService>>(),
            _scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>());
        var credentials = await _keyService.GetHubSigningCredentialsAsync();

        string MintHubToken(bool includeId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = HostingConfig.Current.RootDomain,
            Audience = NodeDomain,
            Expires = DateTime.UtcNow.AddMinutes(15),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = credentials,
            Claims = includeId
                ? new Dictionary<string, object>
                {
                    ["sub"] = hubUserId.ToString(), ["name"] = "Replay Test", ["subscription"] = string.Empty,
                    ["protocol"] = ValourFederation.ProtocolVersion, ["jti"] = Guid.NewGuid().ToString("N"),
                    ["memberships"] = Array.Empty<string>(),
                }
                : new Dictionary<string, object>
                {
                    ["sub"] = hubUserId.ToString(), ["name"] = "Replay Test", ["subscription"] = string.Empty,
                    ["protocol"] = ValourFederation.ProtocolVersion, ["memberships"] = Array.Empty<string>(),
                },
        });

        try
        {
            var hubToken = MintHubToken(includeId: true);
            var first = await nodeService.ExchangeAsync(hubToken, "127.0.0.1");
            Assert.True(first.Success, first.Message);

            var replay = await nodeService.ExchangeAsync(hubToken, "127.0.0.1");
            Assert.False(replay.Success);

            var withoutId = await nodeService.ExchangeAsync(MintHubToken(includeId: false), "127.0.0.1");
            Assert.False(withoutId.Success);
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
    public async Task NodeVerification_RejectsOversizedDescriptor_WithoutEchoingErrors()
    {
        const string domain = "oversized-descriptor.example.com";
        var registration = await _hubService.RegisterNodeAsync(ISharedUser.VictorUserId, domain);
        Assert.True(registration.Success, registration.Message);

        try
        {
            var strictHub = new FederationHubService(
                _db,
                _keyService,
                new TestHttpClientFactory(new StaticJsonHandler(new
                {
                    domain,
                    challenge = registration.Data!.Challenge,
                    protocolVersion = ValourFederation.ProtocolVersion,
                    publicJwk = _nodeJwk,
                    padding = new string('x', 70 * 1024),
                })),
                _scope.ServiceProvider.GetRequiredService<ILogger<FederationHubService>>());

            var verification = await strictHub.VerifyNodeAsync(ISharedUser.VictorUserId, domain);

            Assert.False(verification.Success);
            Assert.DoesNotContain("too large", verification.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Valour.Database.FederatedNodeStatus.PendingVerification,
                (await _db.FederatedNodes.FindAsync(domain))!.Status);
        }
        finally
        {
            await _db.FederatedNodes.Where(x => x.Domain == domain).ExecuteDeleteAsync();
            _db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public void NodeDomains_UseTheDefaultHttpsPortOutsideDevelopment()
    {
        var previousInsecure = FederationConfig.Current.AllowInsecure;
        try
        {
            FederationConfig.Current.AllowInsecure = false;
            Assert.Null(FederationHubService.NormalizeDomain("node.example.com:8443"));
            Assert.Equal("node.example.com", FederationHubService.NormalizeDomain("node.example.com:443"));
            Assert.Equal("node.example.com", FederationHubService.NormalizeDomain("Node.Example.com"));

            FederationConfig.Current.AllowInsecure = true;
            Assert.Equal("localhost:5100", FederationHubService.NormalizeDomain("localhost:5100"));
        }
        finally
        {
            FederationConfig.Current.AllowInsecure = previousInsecure;
        }
    }

    [Fact]
    public async Task PlanetRegistry_AppliesPlanetMetadataLimits()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        var tooLong = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest
        {
            Name = new string('n', 33),
        });
        Assert.False(tooLong.Success);

        var reserve = await _registry.ReserveAsync(domain!, new FederatedPlanetStubRequest { Name = "Within limits" });
        Assert.True(reserve.Success, reserve.Message);

        var longDescription = await _registry.UpsertAsync(domain!, reserve.Data!.Id, new FederatedPlanetStubRequest
        {
            Name = "Within limits",
            Description = new string('d', 5000),
        });
        Assert.False(longDescription.Success);
    }
}
