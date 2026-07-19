using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Tests.Services;

/// <summary>
/// Round-trips a planet through export → delete → import on one instance and
/// verifies that same-domain restores retain ids while cross-domain imports
/// remap node-local objects. This is the data-fidelity core of migration.
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
    private long _threadId;
    private long _threadCommentId;
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

        var memberId = await _db.PlanetMembers.Where(x => x.PlanetId == _planet.Id && x.UserId == _owner.Id)
            .Select(x => x.Id)
            .SingleAsync();
        await _db.PlanetInvites.AddAsync(new Valour.Database.PlanetInvite
        {
            // This integration database is shared. A fixed primary key made a
            // later run fail if an earlier run was interrupted before cleanup.
            Id = Guid.NewGuid().ToString("N")[..8], PlanetId = _planet.Id, IssuerId = _owner.Id,
            TimeCreated = DateTime.UtcNow, TimeExpires = DateTime.UtcNow.AddDays(1),
        });
        await _db.UserChannelStates.AddAsync(new Valour.Database.UserChannelState
        {
            UserId = _owner.Id, ChannelId = _chatChannel.Id, PlanetId = _planet.Id,
            PlanetMemberId = memberId, LastViewedTime = DateTime.UtcNow.AddMinutes(-5),
        });
        _threadId = IdManager.Generate();
        _threadCommentId = IdManager.Generate();
        await _db.PlanetThreads.AddAsync(new Valour.Database.PlanetThread
        {
            Id = _threadId, PlanetId = _planet.Id, AuthorUserId = _owner.Id,
            AuthorMemberId = memberId, Title = "snapshot thread", Content = "preserve boosts",
            TimeCreated = DateTime.UtcNow, BoostCount = 1,
        });
        await _db.ThreadComments.AddAsync(new Valour.Database.ThreadComment
        {
            Id = _threadCommentId, PlanetId = _planet.Id, ThreadId = _threadId,
            AuthorUserId = _owner.Id, AuthorMemberId = memberId, Content = "snapshot comment",
            TimeCreated = DateTime.UtcNow, BoostCount = 1,
        });
        await _db.ThreadBoosts.AddAsync(new Valour.Database.ThreadBoost
        {
            Id = IdManager.Generate(), PlanetId = _planet.Id, ThreadId = _threadId,
            UserId = _owner.Id, CreatedAt = DateTime.UtcNow,
        });
        await _db.ThreadCommentBoosts.AddAsync(new Valour.Database.ThreadCommentBoost
        {
            Id = IdManager.Generate(), PlanetId = _planet.Id, ThreadId = _threadId,
            CommentId = _threadCommentId, UserId = _owner.Id, CreatedAt = DateTime.UtcNow,
        });

        var dbPlanet = await _db.Planets.FindAsync(_planet.Id);
        Assert.NotNull(dbPlanet);
        dbPlanet!.SelfHostedVoice = true;
        dbPlanet.EnableWiki = true;
        dbPlanet.PublicWiki = true;
        dbPlanet.Vanity = "snapshot" + Guid.NewGuid().ToString("N")[..8];
        dbPlanet.PinnedThreadId = _threadId;

        await _db.PlanetWikiPages.AddAsync(new Valour.Database.PlanetWikiPage
        {
            Id = IdManager.Generate(),
            PlanetId = _planet.Id,
            Slug = "migration-notes",
            Title = "Migration notes",
            Content = "This page must retain its placement.",
            Position = 7,
            IsPublished = true,
            Version = 1,
            TimeCreated = DateTime.UtcNow,
            CreatedByUserId = _owner.Id,
        });

        var triggerId = Guid.NewGuid();
        await _db.AutomodTriggers.AddAsync(new Valour.Database.AutomodTrigger
        {
            Id = triggerId,
            PlanetId = _planet.Id,
            MemberAddedBy = memberId,
            Type = AutomodTriggerType.Blacklist,
            Name = "snapshot trigger",
            TriggerWords = "unportable",
            RunForEveryone = true,
        });
        await _db.SaveChangesAsync();

        await _db.AutomodActions.AddAsync(new Valour.Database.AutomodAction
        {
            Id = Guid.NewGuid(),
            PlanetId = _planet.Id,
            TriggerId = triggerId,
            MemberAddedBy = memberId,
            TargetMemberId = memberId,
            ActionType = AutomodActionType.BlockMessage,
            Strikes = 1,
            Message = "blocked by snapshot policy",
            ResponseChannelId = _chatChannel.Id,
        });
        await _db.AutomodLogs.AddAsync(new Valour.Database.AutomodLog
        {
            Id = Guid.NewGuid(),
            PlanetId = _planet.Id,
            TriggerId = triggerId,
            MemberId = memberId,
            MessageId = _messageIds[0],
            TimeTriggered = DateTime.UtcNow,
        });
        await _db.ModerationAuditLogs.AddAsync(new Valour.Database.ModerationAuditLog
        {
            Id = IdManager.Generate(),
            PlanetId = _planet.Id,
            ActorUserId = _owner.Id,
            TargetUserId = _owner.Id,
            TargetMemberId = memberId,
            MessageId = _messageIds[0],
            TriggerId = triggerId,
            Source = ModerationActionSource.Automod,
            ActionType = ModerationActionType.BlockMessage,
            Details = "Snapshot audit record",
            TimeCreated = DateTime.UtcNow,
        });
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
        await _db.PlanetInvites.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetStorageConfigs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetVoiceConfigs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.AutomodLogs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.AutomodActions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.AutomodTriggers.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.ModerationAuditLogs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetWikiRevisions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetWikiPages.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        var threadIds = await _db.PlanetThreads.Where(x => x.PlanetId == planetId).Select(x => x.Id).ToListAsync();
        await _db.ThreadAttachments.Where(x => threadIds.Contains(x.ThreadId)).ExecuteDeleteAsync();
        await _db.ThreadCommentBoosts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.ThreadComments.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.ThreadBoosts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.PlanetThreads.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
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
        Assert.Single(snap.Invites);
        Assert.Single(snap.UserChannelStates);
        Assert.Single(snap.ThreadBoosts);
        Assert.Single(snap.ThreadCommentBoosts);
        Assert.Single(snap.WikiPages);
        Assert.Single(snap.AutomodTriggers);
        Assert.Single(snap.AutomodActions);
        Assert.Single(snap.AutomodLogs);
        Assert.Single(snap.ModerationAuditLogs);
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
        Assert.True(planet.SelfHostedVoice);
        Assert.True(planet.EnableWiki);
        Assert.True(planet.PublicWiki);
        Assert.NotNull(planet.Vanity);
        Assert.Equal(_threadId, planet.PinnedThreadId);

        // Same-domain restore preserves the complete structural graph and ids.
        Assert.Equal(channelCount, await _db.Channels.IgnoreQueryFilters().CountAsync(x => x.PlanetId == _planet.Id));
        Assert.Equal(roleCount, await _db.PlanetRoles.CountAsync(x => x.PlanetId == _planet.Id));
        Assert.Equal(memberCount, await _db.PlanetMembers.IgnoreQueryFilters().CountAsync(x => x.PlanetId == _planet.Id));
        Assert.Single(await _db.PlanetInvites.Where(x => x.PlanetId == _planet.Id).ToListAsync());
        var restoredState = Assert.Single(await _db.UserChannelStates.Where(x => x.PlanetId == _planet.Id).ToListAsync());
        Assert.Equal(_chatChannel.Id, restoredState.ChannelId);
        Assert.Single(await _db.ThreadBoosts.Where(x => x.PlanetId == _planet.Id && x.ThreadId == _threadId).ToListAsync());
        Assert.Single(await _db.ThreadCommentBoosts.Where(x => x.PlanetId == _planet.Id && x.CommentId == _threadCommentId).ToListAsync());
        var wikiPage = Assert.Single(await _db.PlanetWikiPages.Where(x => x.PlanetId == _planet.Id).ToListAsync());
        Assert.Equal((uint)7, wikiPage.Position);
        Assert.Single(await _db.AutomodTriggers.Where(x => x.PlanetId == _planet.Id && x.Type == AutomodTriggerType.Blacklist).ToListAsync());
        Assert.Single(await _db.AutomodActions.Where(x => x.PlanetId == _planet.Id && x.ActionType == AutomodActionType.BlockMessage).ToListAsync());
        Assert.Single(await _db.AutomodLogs.Where(x => x.PlanetId == _planet.Id).ToListAsync());
        Assert.Single(await _db.ModerationAuditLogs.Where(x => x.PlanetId == _planet.Id && x.Source == ModerationActionSource.Automod).ToListAsync());

        // Messages retain their ids for this same-domain restore.
        var messages = await _db.Messages.IgnoreQueryFilters()
            .Where(x => x.PlanetId == _planet.Id).OrderBy(x => x.Content).ToListAsync();
        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.Contains(m.Id, _messageIds));
        Assert.Equal("snapshot message 0", messages[0].Content);

        // The default channel kept its id.
        Assert.True(await _db.Channels.IgnoreQueryFilters().AnyAsync(x => x.Id == _chatChannel.Id && x.PlanetId == _planet.Id));
    }

    [Fact]
    public async Task Import_CrossDomain_RemapsNodeLocalIdsAndReferences()
    {
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success, export.Message);
        var snapshot = export.Data!;

        var sourceChannelId = _chatChannel.Id;
        var sourceMessageId = snapshot.Messages.Single(x => x.Content == "snapshot message 0").Id;
        var sourceThreadId = _threadId;
        var sourceCommentId = _threadCommentId;
        var sourceInviteCode = snapshot.Invites.Single().Id;
        var sourceTriggerId = snapshot.AutomodTriggers.Single().Id;

        // This is a community-origin snapshot. Planet and user ids are global,
        // but every child row id belongs only to the originating node.
        snapshot.SourceDomain = "community.example";
        await DeletePlanetGraphAsync(_planet.Id);
        _db.ChangeTracker.Clear();

        var import = await _snapshotService.ImportAsync(snapshot);
        Assert.True(import.Success, import.Message);

        var importedChannel = await _db.Channels.IgnoreQueryFilters()
            .SingleAsync(x => x.PlanetId == _planet.Id && x.IsDefault);
        var importedMessage = await _db.Messages.IgnoreQueryFilters()
            .SingleAsync(x => x.PlanetId == _planet.Id && x.Content == "snapshot message 0");
        var importedThread = await _db.PlanetThreads.SingleAsync(x => x.PlanetId == _planet.Id);
        var importedComment = await _db.ThreadComments.SingleAsync(x => x.PlanetId == _planet.Id);
        var importedTrigger = await _db.AutomodTriggers.SingleAsync(x => x.PlanetId == _planet.Id);
        var importedAction = await _db.AutomodActions.SingleAsync(x => x.PlanetId == _planet.Id);
        var importedInvite = await _db.PlanetInvites.SingleAsync(x => x.PlanetId == _planet.Id);
        var importedPlanet = await _db.Planets.IgnoreQueryFilters().SingleAsync(x => x.Id == _planet.Id);

        Assert.NotEqual(sourceChannelId, importedChannel.Id);
        Assert.NotEqual(sourceMessageId, importedMessage.Id);
        Assert.NotEqual(sourceThreadId, importedThread.Id);
        Assert.NotEqual(sourceCommentId, importedComment.Id);
        Assert.NotEqual(sourceTriggerId, importedTrigger.Id);
        Assert.NotEqual(sourceInviteCode, importedInvite.Id);

        Assert.Equal(importedChannel.Id, importedMessage.ChannelId);
        Assert.Equal(importedThread.Id, importedComment.ThreadId);
        Assert.Equal(importedThread.Id, importedPlanet.PinnedThreadId);
        Assert.Equal(importedTrigger.Id, importedAction.TriggerId);
        Assert.Equal(importedChannel.Id, importedAction.ResponseChannelId);
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

    [Fact]
    public async Task Import_RejectsSnapshotsThatInjectUnreferencedUsers()
    {
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success, export.Message);

        export.Data!.Users.Add(new PlanetSnapshotUser
        {
            Id = IdManager.Generate(), Name = "Injected", Tag = "0000",
        });

        var import = await _snapshotService.ImportAsync(export.Data);
        Assert.False(import.Success);
        Assert.Contains("user records", import.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_RejectsCrossPlanetRows()
    {
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success, export.Message);

        export.Data!.Channels[0].PlanetId = IdManager.Generate();

        var import = await _snapshotService.ImportAsync(export.Data);
        Assert.False(import.Success);
        Assert.Contains("different planet", import.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_RejectsCyclicHierarchyRows()
    {
        var export = await _snapshotService.ExportAsync(_planet.Id);
        Assert.True(export.Success, export.Message);

        // Hierarchy FKs allow this shape, but clients traverse these trees and
        // must never receive a cycle from an untrusted federation snapshot.
        export.Data!.Channels[0].ParentId = export.Data.Channels[0].Id;

        var import = await _snapshotService.ImportAsync(export.Data);

        Assert.False(import.Success);
        Assert.Contains("cyclic hierarchy", import.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_WithEncryptedPlanetConfiguration_FailsBeforeMigrationCanLoseIt()
    {
        await _db.PlanetStorageConfigs.AddAsync(new Valour.Database.PlanetStorageConfig
        {
            PlanetId = _planet.Id,
            Endpoint = "https://storage.example.invalid",
            Bucket = "planet",
            Region = "us-east-1",
            AccessKeyEncrypted = "source-only-encrypted-key",
            SecretKeyEncrypted = "source-only-encrypted-secret",
            PublicBaseUrl = "https://media.example.invalid",
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var export = await _snapshotService.ExportAsync(_planet.Id);

        Assert.False(export.Success);
        Assert.Contains("encrypted storage or voice", export.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_WithThreadAttachment_FailsBeforeMigrationCanLoseIt()
    {
        var threadId = IdManager.Generate();
        await _db.PlanetThreads.AddAsync(new Valour.Database.PlanetThread
        {
            Id = threadId,
            PlanetId = _planet.Id,
            AuthorUserId = _owner.Id,
            Title = "Attached thread",
            Content = "This attachment must not be silently omitted.",
            TimeCreated = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _db.ThreadAttachments.AddAsync(new Valour.Database.ThreadAttachment
        {
            Id = IdManager.Generate(),
            ThreadId = threadId,
            Type = MessageAttachmentType.File,
            Location = "https://example.invalid/thread-file",
            FileName = "thread-file.txt",
        });
        await _db.SaveChangesAsync();

        var export = await _snapshotService.ExportAsync(_planet.Id);

        Assert.False(export.Success);
        Assert.Contains("thread attachments", export.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_WithCustomPlanetAssets_FailsBeforeMigrationCanLoseThem()
    {
        var planet = await _db.Planets.FindAsync(_planet.Id);
        Assert.NotNull(planet);
        planet!.HasCustomIcon = true;
        await _db.SaveChangesAsync();

        var export = await _snapshotService.ExportAsync(_planet.Id);

        Assert.False(export.Success);
        Assert.Contains("icon, background, or emoji", export.Message, StringComparison.OrdinalIgnoreCase);
    }
}
