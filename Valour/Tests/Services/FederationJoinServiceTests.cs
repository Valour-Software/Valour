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

    private FederationConfig _previousConfig;
    private long _userId;
    private long _stubId;

    public FederationJoinServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _join = _scope.ServiceProvider.GetRequiredService<FederationJoinService>();
    }

    public async ValueTask InitializeAsync()
    {
        _previousConfig = FederationConfig.Current;
        _ = new FederationConfig { HubEnabled = true, AllowInsecure = true };
        _userId = _fixture.Client.Me.Id;
        _stubId = IdManager.Generate();

        await _db.FederatedNodes.AddAsync(new Valour.Database.FederatedNode
        {
            Domain = NodeDomain, OwnerId = ISharedUser.VictorUserId, NodePublicJwk = "{}",
            Status = Valour.Database.FederatedNodeStatus.Active, CreatedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
        });
        await _db.FederatedPlanetStubs.AddAsync(new Valour.Database.FederatedPlanetStub
        {
            Id = _stubId, NodeDomain = NodeDomain, Name = "Community Planet", OwnerId = ISharedUser.VictorUserId,
            Discoverable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _db.FederatedMemberships.RemoveRange(_db.FederatedMemberships.Where(x => x.UserId == _userId));
        _db.FederatedAcceptedDomains.RemoveRange(_db.FederatedAcceptedDomains.Where(x => x.UserId == _userId));
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
    public async Task Resolve_NonFederatedPlanet_ReturnsNull()
    {
        Assert.Null(await _join.ResolveAsync(_userId, IdManager.Generate()));
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
}
