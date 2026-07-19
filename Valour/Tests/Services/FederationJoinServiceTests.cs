using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

/// <summary>
/// The hub-side join flow for community-hosted planets: a user must accept the
/// node's domain before joining, and the hub records the membership.
/// </summary>
[Collection("ApiCollection")]
public class FederationJoinServiceTests : IAsyncLifetime
{
    private const string NodeDomain = "join-test.example.com";

    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly FederationJoinService _join;
    private readonly FederationInviteService _invites;
    private readonly FederationKeyService _keyService;

    private FederationConfig _previousConfig;
    private long _userId;
    private long _stubId;

    public FederationJoinServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _join = _scope.ServiceProvider.GetRequiredService<FederationJoinService>();
        _invites = _scope.ServiceProvider.GetRequiredService<FederationInviteService>();
        _keyService = _scope.ServiceProvider.GetRequiredService<FederationKeyService>();
    }

    public async ValueTask InitializeAsync()
    {
        _previousConfig = FederationConfig.Current;
        _ = new FederationConfig { HubEnabled = true, AllowInsecure = true };
        _userId = _fixture.Client.Me.Id;
        _stubId = IdManager.Generate();
        await _keyService.EnsureKeysAsync();

        await _db.FederatedNodes.AddAsync(new Valour.Database.FederatedNode
        {
            Domain = NodeDomain, OwnerId = ISharedUser.VictorUserId, NodePublicJwk = "{}",
            Status = Valour.Database.FederatedNodeStatus.Active, CreatedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
        });
        await _db.FederatedPlanetStubs.AddAsync(new Valour.Database.FederatedPlanetStub
        {
            Id = _stubId, NodeDomain = NodeDomain, Name = "Community Planet", OwnerId = ISharedUser.VictorUserId,
            Public = true, Discoverable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _db.FederatedMemberships.RemoveRange(_db.FederatedMemberships.Where(x => x.UserId == _userId));
        _db.FederatedAcceptedDomains.RemoveRange(_db.FederatedAcceptedDomains.Where(x => x.UserId == _userId));
        _db.FederatedInviteRedemptions.RemoveRange(_db.FederatedInviteRedemptions.Where(x => x.PlanetId == _stubId));
        _db.FederatedInviteGrants.RemoveRange(_db.FederatedInviteGrants.Where(x => x.PlanetId == _stubId));
        _db.FederatedPlanetStubs.RemoveRange(_db.FederatedPlanetStubs.Where(x => x.Id == _stubId));
        _db.FederatedNodes.RemoveRange(_db.FederatedNodes.Where(x => x.Domain == NodeDomain));
        await _db.SaveChangesAsync();
        FederationConfig.Current = _previousConfig;
        _scope.Dispose();
    }

    [Fact]
    public async Task Join_RequiresAcceptedDomain_ThenRecordsMembership()
    {
        // Resolve shows the domain not yet accepted.
        var location = await _join.ResolveAsync(_userId, _stubId);
        Assert.NotNull(location);
        Assert.Equal(NodeDomain, location!.NodeDomain);
        Assert.False(location.DomainAccepted);

        // Joining before accepting is refused.
        var early = await _join.JoinAsync(_userId, _stubId);
        Assert.False(early.Success);

        // Accept the domain, then join succeeds and is recorded.
        await _join.AcceptDomainAsync(_userId, NodeDomain);
        var joined = await _join.JoinAsync(_userId, _stubId);
        Assert.True(joined.Success, joined.Message);

        var memberships = await _join.GetMembershipsAsync(_userId);
        Assert.Contains(memberships, m => m.PlanetId == _stubId && m.NodeDomain == NodeDomain);
    }

    [Fact]
    public async Task AcceptDomain_RejectsUnregisteredCommunityDomain()
    {
        const string unknownDomain = "not-a-community.example.com";

        var accepted = await _join.AcceptDomainAsync(_userId, unknownDomain);

        Assert.False(accepted.Success);
        Assert.DoesNotContain(unknownDomain, await _join.GetAcceptedDomainsAsync(_userId));
    }

    [Fact]
    public async Task Resolve_NonFederatedPlanet_ReturnsNull()
    {
        Assert.Null(await _join.ResolveAsync(_userId, IdManager.Generate()));
    }

    [Fact]
    public async Task Join_PrivateOrUnlistedPlanet_DoesNotCreateHubMembership()
    {
        var stub = await _db.FederatedPlanetStubs.FindAsync(_stubId);
        stub!.Public = false;
        stub.Discoverable = false;
        await _db.SaveChangesAsync();

        await _join.AcceptDomainAsync(_userId, NodeDomain);
        var join = await _join.JoinAsync(_userId, _stubId);

        Assert.False(join.Success);
        Assert.Null(await _join.ResolveAsync(_userId, _stubId));
        Assert.DoesNotContain(await _join.GetMembershipsAsync(_userId), m => m.PlanetId == _stubId);
    }

    [Fact]
    public async Task Owner_CanIssueAndRevokeRecipientBoundPrivateGrant()
    {
        var stub = await _db.FederatedPlanetStubs.FindAsync(_stubId);
        stub!.Public = false;
        stub.Discoverable = false;
        await _db.SaveChangesAsync();

        var created = await _invites.CreateAsync(ISharedUser.VictorUserId, new FederatedInviteGrantCreateRequest
        {
            PlanetId = _stubId,
            IntendedUserId = _userId,
            MaxUses = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });

        Assert.True(created.Success, created.Message);
        Assert.False(string.IsNullOrWhiteSpace(created.Data!.Grant));
        Assert.Equal(_userId, created.Data.IntendedUserId);

        var stored = await _db.FederatedInviteGrants.FindAsync(created.Data.GrantId);
        Assert.NotNull(stored);
        Assert.Equal(NodeDomain, stored!.NodeDomain);
        Assert.Equal(0, stored.Uses);

        var revoked = await _invites.RevokeAsync(ISharedUser.VictorUserId, created.Data.GrantId);
        Assert.True(revoked.Success, revoked.Message);
        Assert.NotNull((await _db.FederatedInviteGrants.FindAsync(created.Data.GrantId))!.RevokedAt);
    }

    [Fact]
    public async Task InviteReconciliation_RejectsBackdatedOfflineRedemption()
    {
        var created = await _invites.CreateAsync(ISharedUser.VictorUserId, new FederatedInviteGrantCreateRequest
        {
            PlanetId = _stubId,
            IntendedUserId = _userId,
            MaxUses = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        Assert.True(created.Success, created.Message);

        // A node must not be able to keep a client passport/proof and later
        // claim it redeemed the invite during a long-past outage.
        var reconciled = await _invites.ReconcileRedemptionAsync(NodeDomain, new FederatedInviteRedemptionReport
        {
            GrantId = created.Data!.GrantId,
            UserId = _userId,
            PlanetId = _stubId,
            RedeemedAt = DateTime.UtcNow.AddMinutes(-20),
            Passport = "not-needed-after-timestamp-rejection",
            Proof = "not-needed-after-timestamp-rejection",
        });

        Assert.False(reconciled.Success);
        Assert.Contains("offline window", reconciled.Message, StringComparison.OrdinalIgnoreCase);
        var grant = await _db.FederatedInviteGrants.FindAsync(created.Data.GrantId);
        Assert.Equal(0, grant!.Uses);
        Assert.False(await _db.FederatedMemberships.AnyAsync(x => x.UserId == _userId && x.PlanetId == _stubId));
    }

    [Fact]
    public async Task Leave_RemovesMembership()
    {
        await _join.AcceptDomainAsync(_userId, NodeDomain);
        await _join.JoinAsync(_userId, _stubId);
        await _join.LeaveAsync(_userId, _stubId);

        var memberships = await _join.GetMembershipsAsync(_userId);
        Assert.DoesNotContain(memberships, m => m.PlanetId == _stubId);
    }

    [Fact]
    public async Task HubOnlyJoinService_DoesNotPersistConsentOnACommunityNode()
    {
        var previousHubEnabled = FederationConfig.Current.HubEnabled;
        FederationConfig.Current.HubEnabled = false;
        try
        {
            var accepted = await _join.AcceptDomainAsync(_userId, NodeDomain);

            Assert.False(accepted.Success);
            Assert.Empty(await _join.GetAcceptedDomainsAsync(_userId));
            Assert.Empty(await _join.GetMembershipsAsync(_userId));
        }
        finally
        {
            FederationConfig.Current.HubEnabled = previousHubEnabled;
        }
    }
}
