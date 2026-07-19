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
    private readonly PlanetMemberService _memberService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly FederationMigrationService _migrationService;

    private User _owner = null!;
    private Planet _planet = null!;
    private Channel _channel = null!;
    private PlanetMember _member = null!;
    private FederationConfig _previousConfig = null!;

    public MigrationLockTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _messageService = _scope.ServiceProvider.GetRequiredService<MessageService>();
        _memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _hostedPlanetService = _scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        _migrationService = _scope.ServiceProvider.GetRequiredService<FederationMigrationService>();
    }

    public async ValueTask InitializeAsync()
    {
        // Abort is a hub-only operation. Make the test role explicit instead of
        // inheriting the process-wide setting left by an unrelated fixture.
        _previousConfig = FederationConfig.Current;
        _ = new FederationConfig { HubEnabled = true, AllowInsecure = true };

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
        FederationConfig.Current = _previousConfig;
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

    [Fact]
    public async Task LockedPlanet_RejectsMemberChanges()
    {
        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        dbPlanet!.LockedForMigration = true;
        await _db.SaveChangesAsync();

        _member.Nickname = "late mutation";
        var update = await _memberService.UpdateAsync(_member);
        var delete = await _memberService.DeleteAsync(_member.Id);

        Assert.False(update.Success);
        Assert.False(delete.Success);
        Assert.Contains("read-only", update.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read-only", delete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LockedPlanet_RejectsEveryRemainingPlanetMutationPath()
    {
        var storedInvite = new Valour.Database.PlanetInvite
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            PlanetId = _planet.Id,
            IssuerId = _owner.Id,
            TimeCreated = DateTime.UtcNow,
        };
        await _db.PlanetInvites.AddAsync(storedInvite);
        await _db.SaveChangesAsync();

        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        dbPlanet!.LockedForMigration = true;
        await _db.SaveChangesAsync();

        var permissions = _scope.ServiceProvider.GetRequiredService<PermissionsNodeService>();
        var rules = _scope.ServiceProvider.GetRequiredService<PlanetRuleService>();
        var bans = _scope.ServiceProvider.GetRequiredService<PlanetBanService>();
        var invites = _scope.ServiceProvider.GetRequiredService<PlanetInviteService>();
        var threads = _scope.ServiceProvider.GetRequiredService<ThreadService>();
        var wiki = _scope.ServiceProvider.GetRequiredService<PlanetWikiService>();
        var unread = _scope.ServiceProvider.GetRequiredService<UnreadService>();

        var defaultRole = await _db.PlanetRoles.Where(x => x.PlanetId == _planet.Id)
            .OrderBy(x => x.Id).FirstAsync();
        var permission = await permissions.CreateAsync(new PermissionsNode
        {
            PlanetId = _planet.Id, RoleId = defaultRole.Id, TargetId = _channel.Id,
            TargetType = _channel.ChannelType,
        });
        var reorder = await rules.SetRuleOrderAsync(_planet.Id, Array.Empty<long>());
        var banUpdate = await bans.PutAsync(new PlanetBan { PlanetId = _planet.Id });
        var invite = await invites.CreateAsync(new PlanetInvite(), _member);
        var spoofedInviteDelete = await invites.DeleteAsync(new PlanetInvite
        {
            Id = storedInvite.Id,
            PlanetId = 0,
        });
        var pinThread = await threads.SetPinnedAsync(_planet.Id, IdManager.Generate(), true, _owner.Id);
        var deleteDoc = await wiki.DeleteAsync(_planet.Id, IdManager.Generate());
        var updateReadState = await unread.UpdateReadState(
            _channel.Id, _owner.Id, _planet.Id, _member.Id, DateTime.UtcNow);
        var delete = await _planetService.DeleteAsync(_planet.Id);

        Assert.False(permission.Success);
        Assert.False(reorder.Success);
        Assert.False(banUpdate.Success);
        Assert.False(invite.Success);
        Assert.False(spoofedInviteDelete.Success);
        Assert.False(pinThread.Success);
        Assert.False(deleteDoc.Success);
        Assert.False(updateReadState.Success);
        Assert.False(delete.Success);
        Assert.True(await _db.PlanetInvites.AnyAsync(x => x.Id == storedInvite.Id));
        Assert.All(new[] { permission.Message, reorder.Message, banUpdate.Message, invite.Message,
                spoofedInviteDelete.Message, pinThread.Message, deleteDoc.Message, updateReadState.Message, delete.Message },
            message => Assert.Contains("read-only", message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Abort_CompletedHandoff_IsRejectedToPreventTwoWritableCopies()
    {
        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        dbPlanet!.Public = false;
        dbPlanet.Discoverable = false;
        dbPlanet.LockedForMigration = true;
        await _db.FederatedMigrations.AddAsync(new Valour.Database.FederatedMigration
        {
            PlanetId = _planet.Id,
            TargetDomain = "somewhere.example.com",
            Status = Valour.Database.FederatedMigrationStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            SourcePublic = true,
            SourceDiscoverable = true,
        });
        await _db.SaveChangesAsync();

        var abort = await _migrationService.AbortAsync(_owner.Id, _planet.Id);
        Assert.False(abort.Success);
        Assert.Contains("cannot be aborted", abort.Message, StringComparison.OrdinalIgnoreCase);

        await _db.Entry(dbPlanet).ReloadAsync();
        var migration = await _db.FederatedMigrations.FindAsync(_planet.Id);
        Assert.False(dbPlanet.Public);
        Assert.False(dbPlanet.Discoverable);
        Assert.True(dbPlanet.LockedForMigration);
        Assert.Equal(Valour.Database.FederatedMigrationStatus.Completed, migration!.Status);
    }
}
