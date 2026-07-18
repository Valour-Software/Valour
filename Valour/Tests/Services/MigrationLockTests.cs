using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Config.Configs;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

/// <summary>
/// A planet is read-only while a migration is in progress, so no writes are
/// lost between the snapshot and the handoff. Abort clears the lock.
/// </summary>
[Collection("ApiCollection")]
public class MigrationLockTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly PlanetService _planetService;
    private readonly MessageService _messageService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly FederationMigrationService _migrationService;

    private User _owner = null!;
    private Planet _planet = null!;
    private Channel _channel = null!;
    private PlanetMember _member = null!;

    public MigrationLockTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _messageService = _scope.ServiceProvider.GetRequiredService<MessageService>();
        _hostedPlanetService = _scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        _migrationService = _scope.ServiceProvider.GetRequiredService<FederationMigrationService>();
    }

    public async ValueTask InitializeAsync()
    {
        var userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        var memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _owner = await userService.GetAsync(_fixture.Client.Me.Id);

        var create = await _planetService.CreateAsync(new Planet
        {
            Name = "Lock Test Planet", Description = "read-only during migration", OwnerId = _owner.Id,
        }, _owner);
        _planet = create.Data!;
        _channel = await _db.Channels.AsNoTracking()
            .Where(c => c.PlanetId == _planet.Id && c.IsDefault).Select(c => c.ToModel()).FirstAsync();
        _member = await memberService.GetByUserAsync(_owner.Id, _planet.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.Messages.IgnoreQueryFilters().Where(x => x.PlanetId == _planet.Id).ExecuteDeleteAsync();
        await _db.FederatedMigrations.Where(x => x.PlanetId == _planet.Id).ExecuteDeleteAsync();
        _scope.Dispose();
    }

    private Message NewMessage(string content) => new()
    {
        PlanetId = _planet.Id, ChannelId = _channel.Id, AuthorUserId = _owner.Id,
        AuthorMemberId = _member.Id, Content = content, Fingerprint = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task LockedPlanet_RejectsMessagePost_ThenAbortRestoresWrites()
    {
        // Writable to start.
        var before = await _messageService.PostMessageAsync(NewMessage("before lock"));
        Assert.True(before.Success, before.Message);

        // Lock it (as migration initiate does) and evict the hosted cache.
        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        dbPlanet!.LockedForMigration = true;
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = _planet.Id, TargetDomain = "somewhere.example.com",
            Status = Valour.Database.FederatedMigrationStatus.Pending, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(_planet.Id);

        // Writes are now rejected.
        var during = await _messageService.PostMessageAsync(NewMessage("during migration"));
        Assert.False(during.Success);
        Assert.Contains("read-only", during.Message, StringComparison.OrdinalIgnoreCase);

        // Owner aborts → planet writable again.
        var abort = await _migrationService.AbortAsync(_owner.Id, _planet.Id);
        Assert.True(abort.Success, abort.Message);

        var after = await _messageService.PostMessageAsync(NewMessage("after abort"));
        Assert.True(after.Success, after.Message);
    }

    [Fact]
    public async Task Abort_ByNonOwner_IsRejected()
    {
        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        dbPlanet!.LockedForMigration = true;
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = _planet.Id, TargetDomain = "somewhere.example.com",
            Status = Valour.Database.FederatedMigrationStatus.Pending, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var abort = await _migrationService.AbortAsync(ISharedUser.VictorUserId, _planet.Id);
        Assert.False(abort.Success);
    }
}
