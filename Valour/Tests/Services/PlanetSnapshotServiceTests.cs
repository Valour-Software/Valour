using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

/// <summary>
/// Round-trips a planet through export → delete → import on one instance,
/// asserting the reconstructed graph matches at the original ids. This is the
/// data-fidelity core of planet migration.
/// </summary>
[Collection("ApiCollection")]
public class PlanetSnapshotServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly PlanetService _planetService;
    private readonly PlanetSnapshotService _snapshotService;

    private User _owner = null!;
    private Planet _planet = null!;
    private Channel _chatChannel = null!;
    private readonly List<long> _messageIds = new();

    public PlanetSnapshotServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _snapshotService = _scope.ServiceProvider.GetRequiredService<PlanetSnapshotService>();
    }

    public async ValueTask InitializeAsync()
    {
        var userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _owner = await userService.GetAsync(_fixture.Client.Me.Id);

        var create = await _planetService.CreateAsync(new Planet
        {
            Name = "Snapshot Test Planet",
            Description = "Round-trip fidelity",
            OwnerId = _owner.Id,
        }, _owner);
        Assert.True(create.Success, create.Message);
        _planet = create.Data!;

        _chatChannel = await _db.Channels.AsNoTracking()
            .Where(c => c.PlanetId == _planet.Id && c.IsDefault)
            .Select(c => c.ToModel()).FirstAsync();

        // Insert messages directly (planet posting stages through a worker).
        for (int i = 0; i < 3; i++)
        {
            var id = IdManager.Generate();
            _messageIds.Add(id);
            await _db.Messages.AddAsync(new Valour.Database.Message
            {
                Id = id, PlanetId = _planet.Id, ChannelId = _chatChannel.Id,
                AuthorUserId = _owner.Id, Content = $"snapshot message {i}", TimeSent = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DeletePlanetGraphAsync(_planet.Id);
        _scope.Dispose();
    }

    private async Task DeletePlanetGraphAsync(long planetId)
    {
        var msgIds = await _db.Messages.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).Select(x => x.Id).ToListAsync();
        await _db.MessageAttachments.Where(x => msgIds.Contains(x.MessageId)).ExecuteDeleteAsync();
        await _db.MessageReactions.Where(x => msgIds.Contains(x.MessageId)).ExecuteDeleteAsync();
        await _db.MessageMentions.Where(x => msgIds.Contains(x.MessageId)).ExecuteDeleteAsync();
        await _db.Messages.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.UserChannelStates.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PermissionsNodes.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetBans.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetRules.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetEmojis.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.Channels.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Export_Delete_Import_RestoresPlanetAtSameIds()
    {
        // Capture the pre-migration shape.
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success, export.Message);
        var snap = export.Data!;

        var channelCount = snap.Channels.Count;
        var roleCount = snap.Roles.Count;
        var memberCount = snap.Members.Count;
        Assert.Equal(3, snap.Messages.Count);
        Assert.True(channelCount >= 2);
        Assert.True(roleCount >= 1);
        Assert.True(memberCount >= 1);

        // Tear the planet down entirely.
        await DeletePlanetGraphAsync(_planet.Id);
        Assert.False(await _db.Planets.IgnoreQueryFilters().AnyAsync(x => x.Id == _planet.Id));

        // Reconstruct from the snapshot. Clear the tracker first so this shared
        // context behaves like a fresh destination context (bulk ExecuteDelete
        // above did not untrack the entities CreateAsync tracked).
        _db.ChangeTracker.Clear();
        var import = await _snapshotService.ImportAsync(snap);
        Assert.True(import.Success, import.Message);

        // Same planet id, name, owner.
        var planet = await _db.Planets.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == _planet.Id);
        Assert.NotNull(planet);
        Assert.Equal("Snapshot Test Planet", planet!.Name);
        Assert.Equal(_owner.Id, planet.OwnerId);

        // Structural counts preserved, at the same ids.
        Assert.Equal(channelCount, await _db.Channels.IgnoreQueryFilters().CountAsync(x => x.PlanetId == _planet.Id));
        Assert.Equal(roleCount, await _db.PlanetRoles.CountAsync(x => x.PlanetId == _planet.Id));
        Assert.Equal(memberCount, await _db.PlanetMembers.IgnoreQueryFilters().CountAsync(x => x.PlanetId == _planet.Id));

        // Messages restored verbatim at their original ids.
        var messages = await _db.Messages.IgnoreQueryFilters()
            .Where(x => x.PlanetId == _planet.Id).OrderBy(x => x.Content).ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.Contains(m.Id, _messageIds));
        Assert.Equal("snapshot message 0", messages[0].Content);

        // The default channel kept its id.
        Assert.True(await _db.Channels.IgnoreQueryFilters().AnyAsync(x => x.Id == _chatChannel.Id && x.PlanetId == _planet.Id));
    }

    [Fact]
    public async Task Import_WhenPlanetAlreadyExists_Fails()
    {
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success);

        // Planet still present → import must refuse to clobber it.
        var import = await _snapshotService.ImportAsync(export.Data!);
        Assert.False(import.Success);
    }
}
