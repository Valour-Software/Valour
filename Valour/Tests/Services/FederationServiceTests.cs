using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server;
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

    private FederationConfig _previousConfig;
    private string _nodeJwk = null!;

    public FederationServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _keyService = _scope.ServiceProvider.GetRequiredService<FederationKeyService>();
        _hubService = _scope.ServiceProvider.GetRequiredService<FederationHubService>();
        _registry = _scope.ServiceProvider.GetRequiredService<FederationPlanetRegistryService>();
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
            _db.FederatedNodes.Remove(existing);
            await _db.SaveChangesAsync();
        }

        // Restore whatever federation config was in place before this test.
        FederationConfig.Current = _previousConfig;
        _scope.Dispose();
    }

    private async Task<string> MintNodeTokenAsync(string audience)
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
        Assert.Equal(42, stored.MemberCount);
        Assert.False(stored.Discoverable);
    }

    [Fact]
    public async Task WrongAudienceToken_IsRejected()
    {
        var token = await MintNodeTokenAsync("some-other-hub.example.com");
        var domain = await _hubService.AuthenticateNodeAsync(token);
        Assert.Null(domain);
    }

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
    public async Task Adopt_AtSpecificId_PreservesId_ForMigration()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        // A migrating planet keeps its existing snowflake id.
        var existingId = Valour.Server.Database.IdManager.Generate();
        var adopt = await _registry.AdoptAsync(domain!, existingId, new FederatedPlanetStubRequest
        {
            Name = "Migrated Planet",
            OwnerId = ISharedUser.VictorUserId,
        });

        Assert.True(adopt.Success, adopt.Message);
        Assert.Equal(existingId, adopt.Data!.Id);

        var stored = await _db.FederatedPlanetStubs.FindAsync(existingId);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task Adopt_OfIdOwnedByAnotherNode_IsRejected()
    {
        var token = await MintNodeTokenAsync(HostingConfig.Current.RootDomain);
        var domain = await _hubService.AuthenticateNodeAsync(token);

        var id = Valour.Server.Database.IdManager.Generate();
        var first = await _registry.AdoptAsync(domain!, id, new FederatedPlanetStubRequest
        {
            Name = "First", OwnerId = ISharedUser.VictorUserId,
        });
        Assert.True(first.Success);

        // A different node cannot claim an id another node already owns.
        var second = await _registry.AdoptAsync("attacker-node.example.com", id, new FederatedPlanetStubRequest
        {
            Name = "Stolen", OwnerId = ISharedUser.VictorUserId,
        });
        Assert.False(second.Success);
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
}
