using System.Security.Cryptography;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Exports a planet's complete data to a portable snapshot and reconstructs it
/// on import. Planet and user ids are hub-global and are preserved. Channel ids
/// are preserved too, because encrypted messages and channel keys are signed
/// over the planet and channel they belong to; an import fails if one is taken.
/// All other ids are local to a node, so cross-domain imports generate new ids
/// and rewrite their graph references. Referenced users are hub-global. A
/// community node importing from the hub materializes shadow rows for any it
/// lacks; the hub never creates accounts from a node-supplied snapshot.
/// </summary>
public class PlanetSnapshotService
{
    private readonly ValourDb _db;
    private readonly E2eeServerKeyService _serverKeys;
    private readonly ChatCacheService _chatCache;
    private readonly ILogger<PlanetSnapshotService> _logger;

    public PlanetSnapshotService(ValourDb db, E2eeServerKeyService serverKeys, ChatCacheService chatCache,
        ILogger<PlanetSnapshotService> logger)
    {
        _db = db;
        _serverKeys = serverKeys;
        _chatCache = chatCache;
        _logger = logger;
    }

    public async Task<TaskResult<PlanetSnapshot>> ExportAsync(long planetId)
    {
        var planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Planet not found.");

        // These settings contain Data-Protection-encrypted credentials. They
        // cannot be decrypted by another node, so exporting the planet without
        // them would silently break its media or voice provider after source
        // finalization. Fail before locking or handing anything off instead.
        if (await _db.PlanetStorageConfigs.AsNoTracking().AnyAsync(x => x.PlanetId == planetId) ||
            await _db.PlanetVoiceConfigs.AsNoTracking().AnyAsync(x => x.PlanetId == planetId))
        {
            return TaskResult<PlanetSnapshot>.FromFailure(
                "This planet has encrypted storage or voice configuration that cannot be transferred safely.");
        }

        var tagIds = await _db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId)
            .SelectMany(x => x.Tags.Select(t => t.Id))
            .ToListAsync();

        var channels = await _db.Channels.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var roles = await _db.PlanetRoles.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var permNodes = await _db.PermissionsNodes.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var members = await _db.PlanetMembers.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var emojis = await _db.PlanetEmojis.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var rules = await _db.PlanetRules.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var bans = await _db.PlanetBans.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var invites = await _db.PlanetInvites.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var userChannelStates = await _db.UserChannelStates.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var messages = await _db.Messages.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();

        var messageIds = messages.Select(x => x.Id).ToHashSet();
        var attachments = await _db.MessageAttachments.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();
        var reactions = await _db.MessageReactions.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();
        var mentions = await _db.MessageMentions.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();

        var threads = await _db.PlanetThreads.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var threadComments = await _db.ThreadComments.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var threadBoosts = await _db.ThreadBoosts.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var threadCommentBoosts = await _db.ThreadCommentBoosts.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var wikiPages = await _db.PlanetWikiPages.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var wikiRevisions = await _db.PlanetWikiRevisions.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var automodTriggers = await _db.AutomodTriggers.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var automodActions = await _db.AutomodActions.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var automodLogs = await _db.AutomodLogs.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var moderationAuditLogs = await _db.ModerationAuditLogs.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();

        var channelIds = channels.Select(x => x.Id).ToList();
        var keyGenerations = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId)).ToListAsync();
        var keyBoxes = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => channelIds.Contains(x.ChannelId)).ToListAsync();
        var accessLog = await _db.E2eeAccessLogEntries.AsNoTracking()
            .Where(x => x.Scope == (int)Valour.Sdk.E2ee.AccessLogScope.Planet && x.ScopeId == planetId)
            .OrderBy(x => x.Seq).ToListAsync();
        var automodTerms = await _db.E2eeAutomodTerms.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();

        // A snapshot carries database metadata, not CDN/planet-storage bytes.
        // Importing a foreign CdnBucketItemId would either violate the target's
        // FK or leave a dangling object reference. Thread attachments are not
        // represented in the portable schema at all. Refuse the handoff before
        // any side locks or deletes data rather than silently losing media.
        if (attachments.Any(x => !string.IsNullOrWhiteSpace(x.CdnBucketItemId)))
            return TaskResult<PlanetSnapshot>.FromFailure(
                "This planet has storage-backed message attachments. Move its media before federating it.");

        var threadIds = threads.Select(x => x.Id).ToList();
        if (threadIds.Count > 0 && await _db.ThreadAttachments.AsNoTracking().AnyAsync(x => threadIds.Contains(x.ThreadId)))
            return TaskResult<PlanetSnapshot>.FromFailure(
                "This planet has thread attachments, which cannot yet be transferred safely.");

        // Public image objects are addressed only by the planet/emoji ids in
        // this instance's storage. The snapshot has no portable byte payload,
        // so carrying their flags or emoji rows would create broken assets at
        // the destination. Refuse before a source can be finalized instead.
        if (planet.HasCustomIcon || planet.HasCustomBackground || emojis.Count > 0)
            return TaskResult<PlanetSnapshot>.FromFailure(
                "This planet has custom icon, background, or emoji assets that cannot yet be transferred safely.");

        var snapshot = new PlanetSnapshot
        {
            CreatedAt = DateTime.UtcNow,
            SourceDomain = HostingConfig.Current.RootDomain,
            Planet = new PlanetSnapshotPlanet
            {
                Id = planet.Id,
                OwnerId = planet.OwnerId,
                Name = planet.Name,
                Description = planet.Description,
                Public = planet.Public,
                Discoverable = planet.Discoverable,
                Nsfw = planet.Nsfw,
                HasCustomIcon = planet.HasCustomIcon,
                HasAnimatedIcon = planet.HasAnimatedIcon,
                HasCustomBackground = planet.HasCustomBackground,
                SelfHostedMedia = planet.SelfHostedMedia,
                SelfHostedVoice = planet.SelfHostedVoice,
                EnableThreads = planet.EnableThreads,
                PublicThreads = planet.PublicThreads,
                PinnedThreadId = planet.PinnedThreadId,
                EnableWiki = planet.EnableWiki,
                EnableVillage = planet.EnableVillage,
                PublicWiki = planet.PublicWiki,
                Vanity = planet.Vanity,
                Version = planet.Version,
                TagIds = tagIds,
                EncryptionMode = planet.EncryptionMode,
                EncryptionSharesHistory = planet.EncryptionSharesHistory,
            },
            Channels = channels.Select(c => new PlanetSnapshotChannel
            {
                Id = c.Id, Name = c.Name, Description = c.Description, ChannelType = c.ChannelType,
                LastUpdateTime = c.LastUpdateTime, PlanetId = c.PlanetId, ParentId = c.ParentId,
                RawPosition = c.RawPosition, InheritsPerms = c.InheritsPerms, IsDefault = c.IsDefault,
                Nsfw = c.Nsfw, AssociatedChatChannelId = c.AssociatedChatChannelId, Version = c.Version,
                EncryptionGeneration = c.EncryptionGeneration,
            }).ToList(),
            Roles = roles.Select(r => new PlanetSnapshotRole
            {
                Id = r.Id, FlagBitIndex = r.FlagBitIndex, IsAdmin = r.IsAdmin, PlanetId = r.PlanetId,
                Position = r.Position, IsDefault = r.IsDefault, Permissions = r.Permissions,
                ChatPermissions = r.ChatPermissions, CategoryPermissions = r.CategoryPermissions,
                VoicePermissions = r.VoicePermissions, Color = r.Color, Bold = r.Bold, Italics = r.Italics,
                Name = r.Name, AnyoneCanMention = r.AnyoneCanMention, Version = r.Version,
            }).ToList(),
            PermissionNodes = permNodes.Select(n => new PlanetSnapshotPermNode
            {
                Id = n.Id, PlanetId = n.PlanetId, Code = n.Code, Mask = n.Mask, RoleId = n.RoleId,
                TargetId = n.TargetId, TargetType = n.TargetType,
            }).ToList(),
            Members = members.Select(m => new PlanetSnapshotMember
            {
                Id = m.Id, UserId = m.UserId, PlanetId = m.PlanetId, Nickname = m.Nickname,
                MemberAvatar = m.MemberAvatar, DismissedPinThreadId = m.DismissedPinThreadId,
                TimeLastConnected = m.TimeLastConnected,
                Rf0 = m.RoleMembership.Rf0, Rf1 = m.RoleMembership.Rf1,
                Rf2 = m.RoleMembership.Rf2, Rf3 = m.RoleMembership.Rf3,
            }).ToList(),
            Emojis = emojis.Select(e => new PlanetSnapshotEmoji
            {
                Id = e.Id, PlanetId = e.PlanetId, CreatorUserId = e.CreatorUserId, Name = e.Name, CreatedAt = e.CreatedAt,
            }).ToList(),
            Rules = rules.Select(r => new PlanetSnapshotRule
            {
                Id = r.Id, PlanetId = r.PlanetId, Position = r.Position, Title = r.Title, Description = r.Description,
            }).ToList(),
            Bans = bans.Select(b => new PlanetSnapshotBan
            {
                Id = b.Id, PlanetId = b.PlanetId, IssuerId = b.IssuerId, TargetId = b.TargetId,
                Reason = b.Reason, TimeCreated = b.TimeCreated, TimeExpires = b.TimeExpires,
            }).ToList(),
            Invites = invites.Select(i => new PlanetSnapshotInvite
            {
                Id = i.Id, PlanetId = i.PlanetId, IssuerId = i.IssuerId,
                TimeCreated = i.TimeCreated, TimeExpires = i.TimeExpires,
            }).ToList(),
            UserChannelStates = userChannelStates.Select(s => new PlanetSnapshotUserChannelState
            {
                UserId = s.UserId, ChannelId = s.ChannelId, PlanetId = s.PlanetId,
                PlanetMemberId = s.PlanetMemberId, LastViewedTime = s.LastViewedTime,
            }).ToList(),
            Messages = messages.Select(m => new PlanetSnapshotMessage
            {
                Id = m.Id, PlanetId = m.PlanetId, ReplyToId = m.ReplyToId, AuthorUserId = m.AuthorUserId,
                AuthorMemberId = m.AuthorMemberId, Content = m.Content, TimeSent = m.TimeSent,
                ChannelId = m.ChannelId, EditedTime = m.EditedTime, ImportSource = m.ImportSource,
                EncryptionVersion = m.EncryptionVersion, Envelope = m.Envelope, KeyGeneration = m.KeyGeneration,
                SearchTerms = m.SearchTerms,
            }).ToList(),
            Attachments = attachments.Select(a => new PlanetSnapshotAttachment
            {
                Id = a.Id, MessageId = a.MessageId, SortOrder = a.SortOrder, Type = a.Type,
                CdnBucketItemId = a.CdnBucketItemId, Location = a.Location, MimeType = a.MimeType,
                FileName = a.FileName, Width = a.Width, Height = a.Height, Inline = a.Inline,
                Missing = a.Missing, Data = a.Data, OpenGraphData = a.OpenGraphData,
                PlanetHosted = a.PlanetHosted, ReportedSha256 = a.ReportedSha256, IsSpoiler = a.IsSpoiler,
            }).ToList(),
            Reactions = reactions.Select(r => new PlanetSnapshotReaction
            {
                Id = r.Id, Emoji = r.Emoji, MessageId = r.MessageId, AuthorUserId = r.AuthorUserId,
                AuthorMemberId = r.AuthorMemberId, CreatedAt = r.CreatedAt, ImportSource = r.ImportSource,
            }).ToList(),
            Mentions = mentions.Select(m => new PlanetSnapshotMention
            {
                Id = m.Id, MessageId = m.MessageId, SortOrder = m.SortOrder, Type = m.Type, TargetId = m.TargetId,
            }).ToList(),
            Threads = threads.Select(t => new PlanetSnapshotThread
            {
                Id = t.Id, PlanetId = t.PlanetId, AuthorUserId = t.AuthorUserId, AuthorMemberId = t.AuthorMemberId,
                Title = t.Title, Content = t.Content, TimeCreated = t.TimeCreated, EditedTime = t.EditedTime,
                IsLocked = t.IsLocked, Nsfw = t.Nsfw, BoostCount = t.BoostCount, CommentCount = t.CommentCount,
                ImportSource = t.ImportSource,
            }).ToList(),
            ThreadComments = threadComments.Select(c => new PlanetSnapshotThreadComment
            {
                Id = c.Id, PlanetId = c.PlanetId, ThreadId = c.ThreadId, ParentCommentId = c.ParentCommentId,
                Depth = c.Depth, AuthorUserId = c.AuthorUserId, AuthorMemberId = c.AuthorMemberId, Content = c.Content,
                TimeCreated = c.TimeCreated, EditedTime = c.EditedTime, BoostCount = c.BoostCount, ReplyCount = c.ReplyCount,
                ImportSource = c.ImportSource,
            }).ToList(),
            ThreadBoosts = threadBoosts.Select(b => new PlanetSnapshotThreadBoost
            {
                Id = b.Id, ThreadId = b.ThreadId, PlanetId = b.PlanetId,
                UserId = b.UserId, CreatedAt = b.CreatedAt,
            }).ToList(),
            ThreadCommentBoosts = threadCommentBoosts.Select(b => new PlanetSnapshotThreadCommentBoost
            {
                Id = b.Id, CommentId = b.CommentId, ThreadId = b.ThreadId,
                PlanetId = b.PlanetId, UserId = b.UserId, CreatedAt = b.CreatedAt,
            }).ToList(),
            WikiPages = wikiPages.Select(w => new PlanetSnapshotWikiPage
            {
                Id = w.Id, PlanetId = w.PlanetId, ParentId = w.ParentId, IsFolder = w.IsFolder, Slug = w.Slug,
                PreviousSlug = w.PreviousSlug, Title = w.Title, Content = w.Content, IsPublished = w.IsPublished,
                Position = w.Position, Version = w.Version, TimeCreated = w.TimeCreated, LastEdited = w.LastEdited,
                CreatedByUserId = w.CreatedByUserId, LastEditedByUserId = w.LastEditedByUserId,
                ImportSource = w.ImportSource,
            }).ToList(),
            WikiRevisions = wikiRevisions.Select(r => new PlanetSnapshotWikiRevision
            {
                Id = r.Id, PageId = r.PageId, PlanetId = r.PlanetId, Title = r.Title, Content = r.Content,
                AuthorUserId = r.AuthorUserId, TimeCreated = r.TimeCreated, ImportSource = r.ImportSource,
            }).ToList(),
            AutomodTriggers = automodTriggers.Select(t => new PlanetSnapshotAutomodTrigger
            {
                Id = t.Id, PlanetId = t.PlanetId, MemberAddedBy = t.MemberAddedBy, Name = t.Name,
                Type = t.Type, TriggerWords = t.TriggerWords, RunForEveryone = t.RunForEveryone,
            }).ToList(),
            AutomodActions = automodActions.Select(a => new PlanetSnapshotAutomodAction
            {
                Id = a.Id, Strikes = a.Strikes, UseGlobalStrikes = a.UseGlobalStrikes, TriggerId = a.TriggerId,
                MemberAddedBy = a.MemberAddedBy, ActionType = a.ActionType, PlanetId = a.PlanetId, TargetMemberId = a.TargetMemberId,
                MessageId = a.MessageId, RoleId = a.RoleId, Expires = a.Expires, Message = a.Message,
                ResponseChannelId = a.ResponseChannelId,
            }).ToList(),
            AutomodLogs = automodLogs.Select(l => new PlanetSnapshotAutomodLog
            {
                Id = l.Id, PlanetId = l.PlanetId, TriggerId = l.TriggerId, MemberId = l.MemberId,
                MessageId = l.MessageId, TimeTriggered = l.TimeTriggered,
            }).ToList(),
            ModerationAuditLogs = moderationAuditLogs.Select(l => new PlanetSnapshotModerationAuditLog
            {
                Id = l.Id, PlanetId = l.PlanetId, ActorUserId = l.ActorUserId, TargetUserId = l.TargetUserId,
                TargetMemberId = l.TargetMemberId, MessageId = l.MessageId, TriggerId = l.TriggerId,
                Source = l.Source, ActionType = l.ActionType, Details = l.Details, TimeCreated = l.TimeCreated,
            }).ToList(),
            ChannelKeyGenerations = keyGenerations.Select(g => new PlanetSnapshotChannelKeyGeneration
            {
                ChannelId = g.ChannelId, Generation = g.Generation, Body = g.Body, Signature = g.Signature,
                CreatorUserId = g.CreatorUserId, SealPublicKey = g.SealPublicKey, IndexGeneration = g.IndexGeneration,
                CreatedAt = g.CreatedAt, HasMessages = g.HasMessages,
                HeldSecret = g.HeldSecretProtected is null ? null : _serverKeys.UnprotectHeldSecret(g.HeldSecretProtected),
            }).ToList(),
            ChannelKeyBoxes = keyBoxes.Select(b => new PlanetSnapshotChannelKeyBox
            {
                ChannelId = b.ChannelId, Generation = b.Generation, UserId = b.UserId,
                UserKeyGeneration = b.UserKeyGeneration, Box = b.Box, SharedByUserId = b.SharedByUserId,
                CreatedAt = b.CreatedAt,
            }).ToList(),
            AccessLogEntries = accessLog.Select(e => new PlanetSnapshotAccessLogEntry
            {
                Seq = e.Seq, Body = e.Body, Signature = e.Signature, SignerUserId = e.SignerUserId, CreatedAt = e.CreatedAt,
            }).ToList(),
            AutomodTerms = automodTerms.Select(t => new PlanetSnapshotAutomodTerm
            {
                TriggerId = t.TriggerId, ChannelId = t.ChannelId, IndexGeneration = t.IndexGeneration, Terms = t.Terms,
            }).ToList(),
            ServerPublicKeys = new Dictionary<string, byte[]>(await _serverKeys.GetPublicKeysAsync()),
        };

        // Every user reference must travel with a shadow record. Keeping this
        // complete lets ImportAsync reject snapshots that smuggle unrelated user
        // ids into the destination merely by adding them to Users.
        var userIds = members.Select(m => m.UserId)
            .Concat(messages.Select(m => m.AuthorUserId))
            .Concat(reactions.Select(r => r.AuthorUserId))
            .Concat(emojis.Select(e => e.CreatorUserId))
            .Concat(bans.Select(b => b.IssuerId).Concat(bans.Select(b => b.TargetId)))
            .Concat(invites.Select(i => i.IssuerId))
            .Concat(userChannelStates.Select(s => s.UserId))
            .Concat(threads.Select(t => t.AuthorUserId))
            .Concat(threadComments.Select(c => c.AuthorUserId))
            .Concat(threadBoosts.Select(b => b.UserId))
            .Concat(threadCommentBoosts.Select(b => b.UserId))
            .Concat(wikiPages.Select(w => w.CreatedByUserId))
            .Concat(wikiPages.Where(w => w.LastEditedByUserId.HasValue).Select(w => w.LastEditedByUserId!.Value))
            .Concat(wikiRevisions.Select(r => r.AuthorUserId))
            .Concat(mentions.Where(m => m.Type == MentionType.User).Select(m => m.TargetId))
            .Concat(moderationAuditLogs.Where(l => l.ActorUserId.HasValue).Select(l => l.ActorUserId!.Value))
            .Concat(moderationAuditLogs.Where(l => l.TargetUserId.HasValue).Select(l => l.TargetUserId!.Value))
            .Concat(EncryptionUserIds(snapshot))
            .Append(planet.OwnerId)
            .Distinct()
            .ToList();

        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync();
        snapshot.Users = users.Select(u => new PlanetSnapshotUser
        {
            Id = u.Id, Name = u.Name, Tag = u.Tag, SubscriptionType = u.SubscriptionType,
        }).ToList();

        return TaskResult<PlanetSnapshot>.FromData(snapshot);
    }

    /// <summary>
    /// Reconstructs a planet from a snapshot. The hub-issued planet id remains
    /// unchanged; a cross-domain import remaps every node-local child id.
    /// Fails if the planet already exists locally (migrate/replace explicitly
    /// instead).
    /// </summary>
    /// <param name="snapshot">The snapshot to import.</param>
    /// <param name="createMissingUsers">
    /// True on a community node receiving a planet from the hub: accounts are
    /// hub-issued, so the node creates local shadow rows for them. False on
    /// the hub receiving a pull-back: the hub owns accounts, so a snapshot that
    /// names an account the hub does not have is rejected rather than allowed
    /// to create one.
    /// </param>
    public async Task<TaskResult> ImportAsync(PlanetSnapshot snapshot, bool createMissingUsers = true)
    {
        if (snapshot?.Planet is null)
            return TaskResult.FromFailure("Snapshot has no planet.");

        var snapshotValidation = ValidateSnapshotGraph(snapshot);
        if (!snapshotValidation.Success)
            return snapshotValidation;

        if (await _db.Planets.AnyAsync(x => x.Id == snapshot.Planet.Id))
            return TaskResult.FromFailure("A planet with this id already exists here.");

        var tagIds = snapshot.Planet.TagIds ?? new();
        var tags = await _db.Tags.Where(x => tagIds.Contains(x.Id)).ToListAsync();
        if (tags.Count != tagIds.Count)
            return TaskResult.FromFailure("Snapshot references tags that do not exist on this node.");

        if (!createMissingUsers)
        {
            var userIds = snapshot.Users.Select(x => x.Id).ToList();
            var existingUsers = await _db.Users.CountAsync(x => userIds.Contains(x.Id));
            if (existingUsers != userIds.Count)
                return TaskResult.FromFailure("Snapshot references accounts that do not exist on this server.");
        }

        snapshot.Planet.Vanity = await GetImportableVanityAsync(snapshot.Planet);

        // Planet ids and user ids are issued by the hub. The rest of a
        // snapshot's identifiers belong only to its source node. Keeping them
        // verbatim would allow a community node's local ids to collide with
        // rows which already exist at the destination.
        // Channel ids are kept so encrypted messages and channel keys stay
        // valid; they must not collide with a channel that already exists here.
        var snapshotChannelIds = snapshot.Channels.Select(x => x.Id).ToList();
        if (await _db.Channels.IgnoreQueryFilters().AnyAsync(x => snapshotChannelIds.Contains(x.Id)))
            return TaskResult.FromFailure("A channel id in this planet is already in use here.");

        // Automod trigger ids are kept too: an invite-only planet's membership
        // log approves triggers by id, and those approvals must keep working.
        // They are random GUIDs, so a collision means a malformed snapshot.
        var snapshotTriggerIds = (snapshot.AutomodTriggers ?? new()).Select(x => x.Id).ToList();
        if (snapshotTriggerIds.Count > 0 &&
            await _db.AutomodTriggers.IgnoreQueryFilters().AnyAsync(x => snapshotTriggerIds.Contains(x.Id)))
            return TaskResult.FromFailure("An automod trigger id in this planet is already in use here.");

        if (IsCrossDomainSnapshot(snapshot))
            await RemapLocalObjectIdsAsync(snapshot);

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            if (createMissingUsers)
                await EnsureShadowUsersAsync(snapshot.Users);

            var p = snapshot.Planet;
            await _db.Planets.AddAsync(new Valour.Database.Planet
            {
                Id = p.Id, OwnerId = p.OwnerId, Name = p.Name, Description = p.Description,
                Public = p.Public && PlanetEncryptionService.AllowsPublic(p.EncryptionMode),
                Discoverable = p.Discoverable, Nsfw = p.Nsfw,
                HasCustomIcon = p.HasCustomIcon, HasAnimatedIcon = p.HasAnimatedIcon,
                HasCustomBackground = p.HasCustomBackground, SelfHostedMedia = p.SelfHostedMedia,
                SelfHostedVoice = p.SelfHostedVoice,
                EnableThreads = p.EnableThreads, PublicThreads = p.PublicThreads,
                PinnedThreadId = p.PinnedThreadId, EnableWiki = p.EnableWiki, EnableVillage = p.EnableVillage, PublicWiki = p.PublicWiki,
                Vanity = p.Vanity, Version = p.Version, IsDeleted = false,
                Tags = tags, EncryptionMode = p.EncryptionMode, EncryptionSharesHistory = p.EncryptionSharesHistory,
            });

            await _db.PlanetRoles.AddRangeAsync(snapshot.Roles.Select(r => new Valour.Database.PlanetRole
            {
                Id = r.Id, FlagBitIndex = r.FlagBitIndex, IsAdmin = r.IsAdmin, PlanetId = r.PlanetId,
                Position = r.Position, IsDefault = r.IsDefault, Permissions = r.Permissions,
                ChatPermissions = r.ChatPermissions, CategoryPermissions = r.CategoryPermissions,
                VoicePermissions = r.VoicePermissions, Color = r.Color ?? "#ffffff", Bold = r.Bold, Italics = r.Italics,
                Name = r.Name, AnyoneCanMention = r.AnyoneCanMention, Version = r.Version,
            }));

            await _db.Channels.AddRangeAsync(snapshot.Channels.Select(c => new Valour.Database.Channel
            {
                Id = c.Id, Name = c.Name, Description = c.Description, ChannelType = c.ChannelType,
                LastUpdateTime = DateTime.SpecifyKind(c.LastUpdateTime, DateTimeKind.Utc),
                PlanetId = c.PlanetId, ParentId = c.ParentId, RawPosition = c.RawPosition,
                InheritsPerms = c.InheritsPerms, IsDefault = c.IsDefault, Nsfw = c.Nsfw,
                AssociatedChatChannelId = c.AssociatedChatChannelId, Version = c.Version, IsDeleted = false,
                EncryptionGeneration = c.EncryptionGeneration,
            }));

            await _db.PermissionsNodes.AddRangeAsync(snapshot.PermissionNodes.Select(n => new Valour.Database.PermissionsNode
            {
                Id = n.Id, PlanetId = n.PlanetId, Code = n.Code, Mask = n.Mask, RoleId = n.RoleId,
                TargetId = n.TargetId, TargetType = n.TargetType,
            }));

            await _db.PlanetMembers.AddRangeAsync(snapshot.Members.Select(m => new Valour.Database.PlanetMember
            {
                Id = m.Id, UserId = m.UserId, PlanetId = m.PlanetId, Nickname = m.Nickname,
                MemberAvatar = m.MemberAvatar, DismissedPinThreadId = m.DismissedPinThreadId,
                TimeLastConnected = DateTime.SpecifyKind(m.TimeLastConnected, DateTimeKind.Utc),
                RoleMembership = new PlanetRoleMembership(m.Rf0, m.Rf1, m.Rf2, m.Rf3), IsDeleted = false,
            }));

            await _db.PlanetEmojis.AddRangeAsync(snapshot.Emojis.Select(e => new Valour.Database.PlanetEmoji
            {
                Id = e.Id, PlanetId = e.PlanetId, CreatorUserId = e.CreatorUserId, Name = e.Name,
                CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
            }));

            await _db.PlanetRules.AddRangeAsync(snapshot.Rules.Select(r => new Valour.Database.PlanetRule
            {
                Id = r.Id, PlanetId = r.PlanetId, Position = r.Position, Title = r.Title, Description = r.Description,
            }));

            await _db.PlanetBans.AddRangeAsync(snapshot.Bans.Select(b => new Valour.Database.PlanetBan
            {
                Id = b.Id, PlanetId = b.PlanetId, IssuerId = b.IssuerId, TargetId = b.TargetId,
                Reason = b.Reason ?? string.Empty, TimeCreated = DateTime.SpecifyKind(b.TimeCreated, DateTimeKind.Utc),
                TimeExpires = b.TimeExpires.HasValue ? DateTime.SpecifyKind(b.TimeExpires.Value, DateTimeKind.Utc) : null,
            }));

            await _db.PlanetInvites.AddRangeAsync(snapshot.Invites.Select(i => new Valour.Database.PlanetInvite
            {
                Id = i.Id, PlanetId = i.PlanetId, IssuerId = i.IssuerId,
                TimeCreated = DateTime.SpecifyKind(i.TimeCreated, DateTimeKind.Utc),
                TimeExpires = i.TimeExpires.HasValue ? DateTime.SpecifyKind(i.TimeExpires.Value, DateTimeKind.Utc) : null,
            }));

            await _db.UserChannelStates.AddRangeAsync(snapshot.UserChannelStates.Select(s => new Valour.Database.UserChannelState
            {
                UserId = s.UserId, ChannelId = s.ChannelId, PlanetId = s.PlanetId,
                PlanetMemberId = s.PlanetMemberId,
                LastViewedTime = DateTime.SpecifyKind(s.LastViewedTime, DateTimeKind.Utc),
            }));

            await _db.Messages.AddRangeAsync(snapshot.Messages.Select(m => new Valour.Database.Message
            {
                Id = m.Id, PlanetId = m.PlanetId, ReplyToId = m.ReplyToId, AuthorUserId = m.AuthorUserId,
                AuthorMemberId = m.AuthorMemberId, Content = m.Content,
                TimeSent = DateTime.SpecifyKind(m.TimeSent, DateTimeKind.Utc), ChannelId = m.ChannelId,
                EditedTime = m.EditedTime.HasValue ? DateTime.SpecifyKind(m.EditedTime.Value, DateTimeKind.Utc) : null,
                ImportSource = m.ImportSource, EncryptionVersion = m.EncryptionVersion, Envelope = m.Envelope,
                KeyGeneration = m.KeyGeneration, SearchTerms = m.SearchTerms,
            }));

            await _db.MessageAttachments.AddRangeAsync(snapshot.Attachments.Select(a => new Valour.Database.MessageAttachment
            {
                Id = a.Id, MessageId = a.MessageId, SortOrder = a.SortOrder, Type = a.Type,
                CdnBucketItemId = a.CdnBucketItemId, Location = a.Location, MimeType = a.MimeType,
                FileName = a.FileName, Width = a.Width, Height = a.Height, Inline = a.Inline,
                Missing = a.Missing, Data = a.Data, OpenGraphData = a.OpenGraphData,
                PlanetHosted = a.PlanetHosted, ReportedSha256 = a.ReportedSha256, IsSpoiler = a.IsSpoiler,
            }));

            await _db.MessageReactions.AddRangeAsync(snapshot.Reactions.Select(r => new Valour.Database.MessageReaction
            {
                Id = r.Id, Emoji = r.Emoji, MessageId = r.MessageId, AuthorUserId = r.AuthorUserId,
                AuthorMemberId = r.AuthorMemberId, CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                ImportSource = r.ImportSource,
            }));

            await _db.MessageMentions.AddRangeAsync(snapshot.Mentions.Select(m => new Valour.Database.MessageMention
            {
                Id = m.Id, MessageId = m.MessageId, SortOrder = m.SortOrder, Type = m.Type, TargetId = m.TargetId,
            }));

            // Threads before comments (comments reference the thread).
            await _db.PlanetThreads.AddRangeAsync((snapshot.Threads ?? new()).Select(t => new Valour.Database.PlanetThread
            {
                Id = t.Id, PlanetId = t.PlanetId, AuthorUserId = t.AuthorUserId, AuthorMemberId = t.AuthorMemberId,
                Title = t.Title, Content = t.Content, TimeCreated = DateTime.SpecifyKind(t.TimeCreated, DateTimeKind.Utc),
                EditedTime = t.EditedTime.HasValue ? DateTime.SpecifyKind(t.EditedTime.Value, DateTimeKind.Utc) : null,
                IsLocked = t.IsLocked, Nsfw = t.Nsfw, BoostCount = t.BoostCount, CommentCount = t.CommentCount,
                IsDeleted = false, ImportSource = t.ImportSource,
            }));

            await _db.ThreadComments.AddRangeAsync((snapshot.ThreadComments ?? new()).Select(c => new Valour.Database.ThreadComment
            {
                Id = c.Id, PlanetId = c.PlanetId, ThreadId = c.ThreadId, ParentCommentId = c.ParentCommentId,
                Depth = c.Depth, AuthorUserId = c.AuthorUserId, AuthorMemberId = c.AuthorMemberId, Content = c.Content,
                TimeCreated = DateTime.SpecifyKind(c.TimeCreated, DateTimeKind.Utc),
                EditedTime = c.EditedTime.HasValue ? DateTime.SpecifyKind(c.EditedTime.Value, DateTimeKind.Utc) : null,
                BoostCount = c.BoostCount, ReplyCount = c.ReplyCount, IsDeleted = false, ImportSource = c.ImportSource,
            }));

            await _db.ThreadBoosts.AddRangeAsync((snapshot.ThreadBoosts ?? new()).Select(b => new Valour.Database.ThreadBoost
            {
                Id = b.Id, ThreadId = b.ThreadId, PlanetId = b.PlanetId,
                UserId = b.UserId, CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
            }));

            await _db.ThreadCommentBoosts.AddRangeAsync((snapshot.ThreadCommentBoosts ?? new()).Select(b => new Valour.Database.ThreadCommentBoost
            {
                Id = b.Id, CommentId = b.CommentId, ThreadId = b.ThreadId, PlanetId = b.PlanetId,
                UserId = b.UserId, CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
            }));

            // Wiki pages before revisions (revisions reference the page).
            await _db.PlanetWikiPages.AddRangeAsync((snapshot.WikiPages ?? new()).Select(w => new Valour.Database.PlanetWikiPage
            {
                Id = w.Id, PlanetId = w.PlanetId, ParentId = w.ParentId, IsFolder = w.IsFolder, Slug = w.Slug,
                PreviousSlug = w.PreviousSlug, Title = w.Title, Content = w.Content, IsPublished = w.IsPublished,
                Position = w.Position, Version = w.Version, TimeCreated = DateTime.SpecifyKind(w.TimeCreated, DateTimeKind.Utc),
                LastEdited = w.LastEdited.HasValue ? DateTime.SpecifyKind(w.LastEdited.Value, DateTimeKind.Utc) : null,
                CreatedByUserId = w.CreatedByUserId, LastEditedByUserId = w.LastEditedByUserId, ImportSource = w.ImportSource,
            }));

            await _db.PlanetWikiRevisions.AddRangeAsync((snapshot.WikiRevisions ?? new()).Select(r => new Valour.Database.PlanetWikiRevision
            {
                Id = r.Id, PageId = r.PageId, PlanetId = r.PlanetId, Title = r.Title, Content = r.Content,
                AuthorUserId = r.AuthorUserId, TimeCreated = DateTime.SpecifyKind(r.TimeCreated, DateTimeKind.Utc),
                ImportSource = r.ImportSource,
            }));

            // Automod triggers before actions (actions reference the trigger).
            await _db.AutomodTriggers.AddRangeAsync((snapshot.AutomodTriggers ?? new()).Select(t => new Valour.Database.AutomodTrigger
            {
                Id = t.Id, PlanetId = t.PlanetId, MemberAddedBy = t.MemberAddedBy, Name = t.Name,
                Type = t.Type, TriggerWords = t.TriggerWords, RunForEveryone = t.RunForEveryone,
            }));

            // Automod actions use an explicit database FK to their trigger, but
            // the imported entities intentionally carry only ids (not EF
            // navigation properties). Flush the trigger batch first so EF
            // cannot choose an invalid insert order for a valid snapshot.
            await _db.SaveChangesAsync();

            await _db.AutomodActions.AddRangeAsync((snapshot.AutomodActions ?? new()).Select(a => new Valour.Database.AutomodAction
            {
                Id = a.Id, Strikes = a.Strikes, UseGlobalStrikes = a.UseGlobalStrikes, TriggerId = a.TriggerId,
                MemberAddedBy = a.MemberAddedBy, ActionType = a.ActionType, PlanetId = a.PlanetId, TargetMemberId = a.TargetMemberId,
                MessageId = a.MessageId, RoleId = a.RoleId,
                Expires = a.Expires.HasValue ? DateTime.SpecifyKind(a.Expires.Value, DateTimeKind.Utc) : null,
                Message = a.Message, ResponseChannelId = a.ResponseChannelId,
            }));

            await _db.AutomodLogs.AddRangeAsync((snapshot.AutomodLogs ?? new()).Select(l => new Valour.Database.AutomodLog
            {
                Id = l.Id, PlanetId = l.PlanetId, TriggerId = l.TriggerId, MemberId = l.MemberId,
                MessageId = l.MessageId, TimeTriggered = DateTime.SpecifyKind(l.TimeTriggered, DateTimeKind.Utc),
            }));

            await _db.ModerationAuditLogs.AddRangeAsync((snapshot.ModerationAuditLogs ?? new()).Select(l => new Valour.Database.ModerationAuditLog
            {
                Id = l.Id, PlanetId = l.PlanetId, ActorUserId = l.ActorUserId, TargetUserId = l.TargetUserId,
                TargetMemberId = l.TargetMemberId, MessageId = l.MessageId, TriggerId = l.TriggerId,
                Source = l.Source, ActionType = l.ActionType, Details = l.Details,
                TimeCreated = DateTime.SpecifyKind(l.TimeCreated, DateTimeKind.Utc),
            }));

            await _db.E2eeChannelKeyGenerations.AddRangeAsync(snapshot.ChannelKeyGenerations.Select(g => new Valour.Database.E2eeChannelKeyGeneration
            {
                ChannelId = g.ChannelId, Generation = g.Generation, Body = g.Body, Signature = g.Signature,
                CreatorUserId = g.CreatorUserId, SealPublicKey = g.SealPublicKey, IndexGeneration = g.IndexGeneration,
                CreatedAt = DateTime.SpecifyKind(g.CreatedAt, DateTimeKind.Utc), HasMessages = g.HasMessages,
                HeldSecretProtected = g.HeldSecret is null ? null : _serverKeys.ProtectHeldSecret(g.HeldSecret),
            }));

            await _db.E2eeChannelKeyBoxes.AddRangeAsync(snapshot.ChannelKeyBoxes.Select(b => new Valour.Database.E2eeChannelKeyBox
            {
                ChannelId = b.ChannelId, Generation = b.Generation, UserId = b.UserId,
                UserKeyGeneration = b.UserKeyGeneration, Box = b.Box, SharedByUserId = b.SharedByUserId,
                CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
            }));

            await _db.E2eeAccessLogEntries.AddRangeAsync(snapshot.AccessLogEntries.Select(e => new Valour.Database.E2eeAccessLogEntry
            {
                Scope = (int)Valour.Sdk.E2ee.AccessLogScope.Planet, ScopeId = snapshot.Planet.Id, Seq = e.Seq,
                Body = e.Body, Signature = e.Signature, SignerUserId = e.SignerUserId,
                IsCheckpoint = Valour.Sdk.E2ee.AccessLogRecord.Decode(e.Body).Type ==
                               Valour.Sdk.E2ee.AccessLogEntryType.Checkpoint,
                CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
            }));

            await _db.E2eeAutomodTerms.AddRangeAsync(snapshot.AutomodTerms.Select(t => new Valour.Database.E2eeAutomodTerm
            {
                TriggerId = t.TriggerId, PlanetId = snapshot.Planet.Id, ChannelId = t.ChannelId,
                IndexGeneration = t.IndexGeneration, Terms = t.Terms,
            }));

            await _db.SaveChangesAsync();
            await _serverKeys.ImportPublicKeysAsync(snapshot.ServerPublicKeys);
            await tran.CommitAsync();

            AutomodService.InvalidateRulesCache(snapshot.Planet.Id);
            foreach (var channel in snapshot.Channels)
            {
                _chatCache.ForgetChannel(channel.Id);
                E2eeChannelKeyService.ForgetChannel(channel.Id);
            }

            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Failed to import planet snapshot {PlanetId}", snapshot.Planet.Id);
            return TaskResult.FromFailure($"Import failed: {e.Message}");
        }
    }

    /// <summary>
    /// Applies the same vanity rules as <see cref="PlanetService.SetVanityAsync"/>.
    /// A vanity that is invalid or already used by another planet here is
    /// dropped rather than failing the whole import; the owner can choose a
    /// new one afterwards.
    /// </summary>
    private async Task<string> GetImportableVanityAsync(PlanetSnapshotPlanet planet)
    {
        var vanity = planet.Vanity?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(vanity) || !VanityUtils.ValidateVanity(vanity).Success)
            return null;

        var taken = await _db.Planets.IgnoreQueryFilters()
            .AnyAsync(x => x.Vanity == vanity && x.Id != planet.Id);
        return taken ? null : vanity;
    }

    private static bool IsCrossDomainSnapshot(PlanetSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.SourceDomain) ||
        !string.Equals(snapshot.SourceDomain, HostingConfig.Current.RootDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces every identifier which is local to a node, then rewrites every
    /// reference in the snapshot to the corresponding destination identifier.
    /// This runs only after graph validation, so dictionary lookups below are
    /// deliberate invariants rather than attacker-controlled partial links.
    /// </summary>
    private async Task RemapLocalObjectIdsAsync(PlanetSnapshot snapshot)
    {
        // Encrypted messages and channel keys are signed over their channel id.
        var channels = snapshot.Channels.ToDictionary(x => x.Id, x => x.Id);
        var roles = CreateLocalIdMap(snapshot.Roles, x => x.Id);
        var permissionNodes = CreateLocalIdMap(snapshot.PermissionNodes, x => x.Id);
        var members = CreateLocalIdMap(snapshot.Members, x => x.Id);
        var emojis = CreateLocalIdMap(snapshot.Emojis, x => x.Id);
        var rules = CreateLocalIdMap(snapshot.Rules, x => x.Id);
        var bans = CreateLocalIdMap(snapshot.Bans, x => x.Id);
        var messages = CreateLocalIdMap(snapshot.Messages, x => x.Id);
        var attachments = CreateLocalIdMap(snapshot.Attachments, x => x.Id);
        var reactions = CreateLocalIdMap(snapshot.Reactions, x => x.Id);
        var mentions = CreateLocalIdMap(snapshot.Mentions, x => x.Id);
        var threads = CreateLocalIdMap(snapshot.Threads, x => x.Id);
        var threadComments = CreateLocalIdMap(snapshot.ThreadComments, x => x.Id);
        var threadBoosts = CreateLocalIdMap(snapshot.ThreadBoosts, x => x.Id);
        var threadCommentBoosts = CreateLocalIdMap(snapshot.ThreadCommentBoosts, x => x.Id);
        var wikiPages = CreateLocalIdMap(snapshot.WikiPages, x => x.Id);
        var wikiRevisions = CreateLocalIdMap(snapshot.WikiRevisions, x => x.Id);
        var auditLogs = CreateLocalIdMap(snapshot.ModerationAuditLogs, x => x.Id);
        // Kept so membership log approvals of automod triggers stay valid.
        var triggers = snapshot.AutomodTriggers.ToDictionary(x => x.Id, x => x.Id);
        var actions = CreateLocalGuidMap(snapshot.AutomodActions, x => x.Id);
        var automodLogs = CreateLocalGuidMap(snapshot.AutomodLogs, x => x.Id);

        snapshot.Planet.PinnedThreadId = MapOptional(threads, snapshot.Planet.PinnedThreadId);

        foreach (var channel in snapshot.Channels)
        {
            channel.Id = Map(channels, channel.Id);
            channel.ParentId = MapOptional(channels, channel.ParentId);
            channel.AssociatedChatChannelId = MapOptional(channels, channel.AssociatedChatChannelId);
        }

        foreach (var role in snapshot.Roles)
            role.Id = Map(roles, role.Id);

        foreach (var permissionNode in snapshot.PermissionNodes)
        {
            permissionNode.Id = Map(permissionNodes, permissionNode.Id);
            permissionNode.RoleId = Map(roles, permissionNode.RoleId);
            permissionNode.TargetId = Map(channels, permissionNode.TargetId);
        }

        foreach (var member in snapshot.Members)
        {
            member.Id = Map(members, member.Id);
            member.DismissedPinThreadId = MapOptional(threads, member.DismissedPinThreadId);
        }

        foreach (var emoji in snapshot.Emojis)
            emoji.Id = Map(emojis, emoji.Id);
        foreach (var rule in snapshot.Rules)
            rule.Id = Map(rules, rule.Id);
        foreach (var ban in snapshot.Bans)
            ban.Id = Map(bans, ban.Id);

        // Invite codes are node-local primary keys and are currently limited to
        // eight characters. Generate new cryptographically random codes and
        // check the destination so a cross-domain import cannot claim one.
        var newInviteCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invite in snapshot.Invites)
            invite.Id = await GenerateUniqueInviteCodeAsync(newInviteCodes);

        foreach (var state in snapshot.UserChannelStates)
        {
            state.ChannelId = Map(channels, state.ChannelId);
            state.PlanetMemberId = MapOptional(members, state.PlanetMemberId);
        }

        foreach (var message in snapshot.Messages)
        {
            message.Id = Map(messages, message.Id);
            message.ReplyToId = MapOptional(messages, message.ReplyToId);
            message.AuthorMemberId = MapOptional(members, message.AuthorMemberId);
            message.ChannelId = Map(channels, message.ChannelId);
        }

        foreach (var attachment in snapshot.Attachments)
        {
            attachment.Id = Map(attachments, attachment.Id);
            attachment.MessageId = Map(messages, attachment.MessageId);
        }

        foreach (var reaction in snapshot.Reactions)
        {
            reaction.Id = Map(reactions, reaction.Id);
            reaction.MessageId = Map(messages, reaction.MessageId);
            reaction.AuthorMemberId = MapOptional(members, reaction.AuthorMemberId);
        }

        foreach (var mention in snapshot.Mentions)
        {
            mention.Id = Map(mentions, mention.Id);
            mention.MessageId = Map(messages, mention.MessageId);
            mention.TargetId = mention.Type switch
            {
                MentionType.PlanetMember => Map(members, mention.TargetId),
                MentionType.Channel => Map(channels, mention.TargetId),
                MentionType.Role => Map(roles, mention.TargetId),
                MentionType.User => mention.TargetId,
                _ => throw new InvalidOperationException("Snapshot contains an unknown mention type."),
            };
        }

        foreach (var thread in snapshot.Threads)
        {
            thread.Id = Map(threads, thread.Id);
            thread.AuthorMemberId = MapOptional(members, thread.AuthorMemberId);
        }

        foreach (var comment in snapshot.ThreadComments)
        {
            comment.Id = Map(threadComments, comment.Id);
            comment.ThreadId = Map(threads, comment.ThreadId);
            comment.ParentCommentId = MapOptional(threadComments, comment.ParentCommentId);
            comment.AuthorMemberId = MapOptional(members, comment.AuthorMemberId);
        }

        foreach (var boost in snapshot.ThreadBoosts)
        {
            boost.Id = Map(threadBoosts, boost.Id);
            boost.ThreadId = Map(threads, boost.ThreadId);
        }

        foreach (var boost in snapshot.ThreadCommentBoosts)
        {
            boost.Id = Map(threadCommentBoosts, boost.Id);
            boost.CommentId = Map(threadComments, boost.CommentId);
            boost.ThreadId = Map(threads, boost.ThreadId);
        }

        foreach (var page in snapshot.WikiPages)
        {
            page.Id = Map(wikiPages, page.Id);
            page.ParentId = MapOptional(wikiPages, page.ParentId);
        }

        foreach (var revision in snapshot.WikiRevisions)
        {
            revision.Id = Map(wikiRevisions, revision.Id);
            revision.PageId = Map(wikiPages, revision.PageId);
        }

        foreach (var trigger in snapshot.AutomodTriggers)
        {
            trigger.Id = Map(triggers, trigger.Id);
            trigger.MemberAddedBy = Map(members, trigger.MemberAddedBy);
        }

        foreach (var action in snapshot.AutomodActions)
        {
            action.Id = Map(actions, action.Id);
            action.TriggerId = Map(triggers, action.TriggerId);
            action.MemberAddedBy = Map(members, action.MemberAddedBy);
            action.TargetMemberId = Map(members, action.TargetMemberId);
            action.MessageId = MapOptional(messages, action.MessageId);
            action.RoleId = MapOptional(roles, action.RoleId);
            action.ResponseChannelId = MapOptional(channels, action.ResponseChannelId);
        }

        foreach (var log in snapshot.AutomodLogs)
        {
            log.Id = Map(automodLogs, log.Id);
            log.TriggerId = Map(triggers, log.TriggerId);
            log.MemberId = Map(members, log.MemberId);
            log.MessageId = MapOptional(messages, log.MessageId);
        }

        foreach (var auditLog in snapshot.ModerationAuditLogs)
        {
            auditLog.Id = Map(auditLogs, auditLog.Id);
            auditLog.TargetMemberId = MapOptional(members, auditLog.TargetMemberId);
            auditLog.MessageId = MapOptional(messages, auditLog.MessageId);
            auditLog.TriggerId = MapOptional(triggers, auditLog.TriggerId);
        }

        foreach (var term in snapshot.AutomodTerms)
            term.TriggerId = Map(triggers, term.TriggerId);
    }

    /// <summary>
    /// Users referenced by encryption records: members holding channel keys,
    /// members who shared them, key creators, and membership log signers.
    /// The server's own marker id is not a user.
    /// </summary>
    private static IEnumerable<long> EncryptionUserIds(PlanetSnapshot snapshot) =>
        snapshot.ChannelKeyBoxes.Select(x => x.UserId)
            .Concat(snapshot.ChannelKeyBoxes.Select(x => x.SharedByUserId))
            .Concat(snapshot.ChannelKeyGenerations.Select(x => x.CreatorUserId))
            .Concat(snapshot.AccessLogEntries.Select(x => x.SignerUserId))
            .Where(x => x != Valour.Sdk.E2ee.ChannelKeyGenerationRecord.ServerCreatorId);

    private static Dictionary<long, long> CreateLocalIdMap<T>(IEnumerable<T> source, Func<T, long> getId) =>
        source.ToDictionary(getId, _ => IdManager.Generate());

    private static Dictionary<Guid, Guid> CreateLocalGuidMap<T>(IEnumerable<T> source, Func<T, Guid> getId) =>
        source.ToDictionary(getId, _ => Guid.NewGuid());

    private static long Map(IReadOnlyDictionary<long, long> map, long id) => map[id];

    private static long? MapOptional(IReadOnlyDictionary<long, long> map, long? id) =>
        id.HasValue ? Map(map, id.Value) : null;

    private static Guid Map(IReadOnlyDictionary<Guid, Guid> map, Guid id) => map[id];

    private static Guid? MapOptional(IReadOnlyDictionary<Guid, Guid> map, Guid? id) =>
        id.HasValue ? Map(map, id.Value) : null;

    private async Task<string> GenerateUniqueInviteCodeAsync(ISet<string> newCodes)
    {
        const string characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var code = new char[8];
        while (true)
        {
            for (var i = 0; i < code.Length; i++)
                code[i] = characters[RandomNumberGenerator.GetInt32(characters.Length)];

            var candidate = new string(code);
            if (newCodes.Add(candidate) && !await _db.PlanetInvites.AnyAsync(x => x.Id == candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Hard-deletes a planet's entire data graph — used for the source side of
    /// a full-handoff migration once the destination has imported the snapshot.
    /// </summary>
    public async Task<TaskResult> DeletePlanetDataAsync(long planetId)
    {
        // Messages still waiting to be flushed would reference the deleted planet data
        PlanetMessageWorker.RemoveMessages(x => x.PlanetId == planetId);

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            var msgIds = await _db.Messages.IgnoreQueryFilters()
                .Where(x => x.PlanetId == planetId).Select(x => x.Id).ToListAsync();

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
            await _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            // Threads (children before threads). Attachments are keyed by thread id.
            var threadIds = await _db.PlanetThreads.Where(x => x.PlanetId == planetId).Select(x => x.Id).ToListAsync();
            await _db.ThreadCommentBoosts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.ThreadComments.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.ThreadAttachments.Where(x => threadIds.Contains(x.ThreadId)).ExecuteDeleteAsync();
            await _db.ThreadBoosts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.PlanetThreads.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            // Wiki (revisions before pages).
            await _db.PlanetWikiRevisions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.PlanetWikiPages.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            // Automod (actions + logs before triggers).
            await _db.AutomodActions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.AutomodLogs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.AutomodTriggers.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.ModerationAuditLogs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            // End-to-end encryption records for the planet's channels.
            var channelIds = await _db.Channels.IgnoreQueryFilters()
                .Where(x => x.PlanetId == planetId).Select(x => x.Id).ToListAsync();
            await _db.E2eeChannelKeyBoxes.Where(x => channelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
            await _db.E2eeChannelKeyGenerations.Where(x => channelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
            await _db.E2eeKeyRequests.Where(x => channelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
            await _db.E2eeAutomodTerms.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.MessageProofs.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.E2eeAccessLogEntries
                .Where(x => x.Scope == (int)Valour.Sdk.E2ee.AccessLogScope.Planet && x.ScopeId == planetId)
                .ExecuteDeleteAsync();

            // Voice meetings are provider-local, transient sessions. They must
            // not survive a handoff with a stale planet/channel reference.
            await _db.RealtimeKitMeetings.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            // Economy: reset (Valour-Credits-tied economy never migrates). Transactions
            // reference accounts/currencies, so they go first.
            await _db.Transactions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.EcoAccounts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Currencies.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            await _db.Channels.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();

            await tran.CommitAsync();
            AutomodService.InvalidateRulesCache(planetId);

            foreach (var channelId in channelIds)
            {
                _chatCache.ForgetChannel(channelId);
                E2eeChannelKeyService.ForgetChannel(channelId);
            }
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Failed to delete planet data {PlanetId}", planetId);
            return TaskResult.FromFailure($"Delete failed: {e.Message}");
        }
    }

    /// <summary>
    /// Limits a community-node snapshot to identities the hub itself vouches
    /// for, before the hub imports it during a pull-back. A node controls every
    /// row it sends, so it could otherwise add memberships for arbitrary hub
    /// accounts or recreate deleted accounts under a name of its choosing.
    ///
    /// Membership rows are kept only for <paramref name="memberUserIds"/> (the
    /// planet owner and accounts with a hub-recorded federated membership).
    /// Content whose author is not an existing hub account is attributed to
    /// the Victor system account, matching how the hub reassigns planets of
    /// deleted accounts. Per-account state for unknown accounts (reactions,
    /// boosts, bans against them, read state, user mentions) is dropped.
    /// Thread and comment counters are recomputed from the rows that remain.
    /// The result still passes through the normal graph validation on import.
    /// </summary>
    public async Task RestrictToHubIdentitiesAsync(PlanetSnapshot snapshot, IReadOnlySet<long> memberUserIds)
    {
        NormalizeCollections(snapshot);

        var referenced = CollectReferencedUserIds(snapshot);
        referenced.Add(ISharedUser.VictorUserId);
        var knownUsers = (await _db.Users.AsNoTracking()
                .Where(x => referenced.Contains(x.Id) && !x.IsFederated)
                .Select(x => x.Id)
                .ToListAsync())
            .ToHashSet();

        long MapAuthor(long userId) => knownUsers.Contains(userId) ? userId : ISharedUser.VictorUserId;
        long? MapOptionalUser(long? userId) => userId.HasValue && knownUsers.Contains(userId.Value) ? userId : null;

        // Members: one row per permitted, existing account.
        snapshot.Members = snapshot.Members
            .Where(x => memberUserIds.Contains(x.UserId) && knownUsers.Contains(x.UserId))
            .GroupBy(x => x.UserId)
            .Select(x => x.First())
            .ToList();
        // Duplicate member ids are rejected by graph validation during import.
        var memberUsers = new Dictionary<long, long>();
        foreach (var member in snapshot.Members)
            memberUsers.TryAdd(member.Id, member.UserId);
        var keptMemberUserIds = snapshot.Members.Select(x => x.UserId).ToHashSet();
        var ownerMemberId = snapshot.Members
            .Where(x => x.UserId == snapshot.Planet.OwnerId)
            .Select(x => (long?)x.Id)
            .FirstOrDefault();

        // A member reference survives only if it still points at a kept member
        // of the same (possibly reattributed) author.
        long? MapMember(long? memberId, long userId) =>
            memberId.HasValue && memberUsers.TryGetValue(memberId.Value, out var memberUserId) && memberUserId == userId
                ? memberId
                : null;

        foreach (var message in snapshot.Messages)
        {
            message.AuthorUserId = MapAuthor(message.AuthorUserId);
            message.AuthorMemberId = MapMember(message.AuthorMemberId, message.AuthorUserId);
        }

        snapshot.Reactions = snapshot.Reactions.Where(x => knownUsers.Contains(x.AuthorUserId)).ToList();
        foreach (var reaction in snapshot.Reactions)
            reaction.AuthorMemberId = MapMember(reaction.AuthorMemberId, reaction.AuthorUserId);

        foreach (var thread in snapshot.Threads)
        {
            thread.AuthorUserId = MapAuthor(thread.AuthorUserId);
            thread.AuthorMemberId = MapMember(thread.AuthorMemberId, thread.AuthorUserId);
        }

        foreach (var comment in snapshot.ThreadComments)
        {
            comment.AuthorUserId = MapAuthor(comment.AuthorUserId);
            comment.AuthorMemberId = MapMember(comment.AuthorMemberId, comment.AuthorUserId);
        }

        snapshot.ThreadBoosts = snapshot.ThreadBoosts.Where(x => knownUsers.Contains(x.UserId)).ToList();
        snapshot.ThreadCommentBoosts = snapshot.ThreadCommentBoosts.Where(x => knownUsers.Contains(x.UserId)).ToList();

        foreach (var page in snapshot.WikiPages)
        {
            page.CreatedByUserId = MapAuthor(page.CreatedByUserId);
            page.LastEditedByUserId = MapOptionalUser(page.LastEditedByUserId);
        }

        foreach (var revision in snapshot.WikiRevisions)
            revision.AuthorUserId = MapAuthor(revision.AuthorUserId);

        foreach (var emoji in snapshot.Emojis)
            emoji.CreatorUserId = MapAuthor(emoji.CreatorUserId);

        snapshot.Bans = snapshot.Bans.Where(x => knownUsers.Contains(x.TargetId)).ToList();
        foreach (var ban in snapshot.Bans)
            ban.IssuerId = MapAuthor(ban.IssuerId);

        foreach (var invite in snapshot.Invites)
            invite.IssuerId = MapAuthor(invite.IssuerId);

        snapshot.UserChannelStates = snapshot.UserChannelStates
            .Where(x => keptMemberUserIds.Contains(x.UserId))
            .ToList();
        foreach (var state in snapshot.UserChannelStates)
            state.PlanetMemberId = MapMember(state.PlanetMemberId, state.UserId);

        snapshot.Mentions = snapshot.Mentions
            .Where(x => x.Type switch
            {
                MentionType.User => knownUsers.Contains(x.TargetId),
                MentionType.PlanetMember => memberUsers.ContainsKey(x.TargetId),
                _ => true,
            })
            .ToList();

        foreach (var log in snapshot.ModerationAuditLogs)
        {
            log.ActorUserId = MapOptionalUser(log.ActorUserId);
            log.TargetUserId = MapOptionalUser(log.TargetUserId);
            if (log.TargetMemberId.HasValue && !memberUsers.ContainsKey(log.TargetMemberId.Value))
                log.TargetMemberId = null;
        }

        // Automod rows require a member. Rules added by a removed member are
        // kept under the owner's membership when there is one.
        foreach (var trigger in snapshot.AutomodTriggers.Where(x => !memberUsers.ContainsKey(x.MemberAddedBy) && ownerMemberId.HasValue))
            trigger.MemberAddedBy = ownerMemberId!.Value;
        snapshot.AutomodTriggers = snapshot.AutomodTriggers.Where(x => memberUsers.ContainsKey(x.MemberAddedBy)).ToList();
        var triggerIds = snapshot.AutomodTriggers.Select(x => x.Id).ToHashSet();

        foreach (var action in snapshot.AutomodActions.Where(x => !memberUsers.ContainsKey(x.MemberAddedBy) && ownerMemberId.HasValue))
            action.MemberAddedBy = ownerMemberId!.Value;
        snapshot.AutomodActions = snapshot.AutomodActions
            .Where(x => triggerIds.Contains(x.TriggerId) &&
                        memberUsers.ContainsKey(x.MemberAddedBy) &&
                        memberUsers.ContainsKey(x.TargetMemberId))
            .ToList();
        snapshot.AutomodLogs = snapshot.AutomodLogs
            .Where(x => triggerIds.Contains(x.TriggerId) && memberUsers.ContainsKey(x.MemberId))
            .ToList();

        // Counters are derived data. Recompute them from the rows being
        // imported instead of trusting the node's values.
        var threadBoosts = snapshot.ThreadBoosts.GroupBy(x => x.ThreadId).ToDictionary(x => x.Key, x => x.Count());
        var commentBoosts = snapshot.ThreadCommentBoosts.GroupBy(x => x.CommentId).ToDictionary(x => x.Key, x => x.Count());
        var threadComments = snapshot.ThreadComments.GroupBy(x => x.ThreadId).ToDictionary(x => x.Key, x => x.Count());
        var commentReplies = snapshot.ThreadComments
            .Where(x => x.ParentCommentId.HasValue)
            .GroupBy(x => x.ParentCommentId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        foreach (var thread in snapshot.Threads)
        {
            thread.BoostCount = threadBoosts.GetValueOrDefault(thread.Id);
            thread.CommentCount = threadComments.GetValueOrDefault(thread.Id);
        }

        foreach (var comment in snapshot.ThreadComments)
        {
            comment.BoostCount = commentBoosts.GetValueOrDefault(comment.Id);
            comment.ReplyCount = commentReplies.GetValueOrDefault(comment.Id);
        }

        // Describe the remaining accounts with the hub's own records, never
        // the node-supplied names.
        var remaining = CollectReferencedUserIds(snapshot);
        snapshot.Users = await _db.Users.AsNoTracking()
            .Where(x => remaining.Contains(x.Id))
            .Select(x => new PlanetSnapshotUser
            {
                Id = x.Id, Name = x.Name, Tag = x.Tag, SubscriptionType = x.SubscriptionType,
            })
            .ToListAsync();
    }

    private async Task EnsureShadowUsersAsync(List<PlanetSnapshotUser> users)
    {
        if (users is null || users.Count == 0)
            return;

        var ids = users.Select(u => u.Id).ToList();
        var existing = await _db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        var existingSet = existing.ToHashSet();

        var newUsers = users.Where(u => !existingSet.Contains(u.Id)).ToList();
        foreach (var u in newUsers)
        {
            await _db.Users.AddAsync(new Valour.Database.User
            {
                Id = u.Id, Name = u.Name, Tag = u.Tag ?? "0000",
                TimeJoined = DateTime.UtcNow, TimeLastActive = DateTime.UtcNow,
                Compliance = true, IsFederated = true, SubscriptionType = u.SubscriptionType,
                // Populate the legacy nullable-in-code fields too. Some existing
                // installations enforce them at the database level.
                OldAvatarUrl = string.Empty,
                Status = string.Empty,
                PriorName = string.Empty,
                StarColor1 = string.Empty,
                StarColor2 = string.Empty,
            });
        }

        // UserProfile's foreign key is not represented by an EF navigation, so
        // persist the principals before adding dependent profile rows. ImportAsync
        // wraps this in its transaction, preserving all-or-nothing behavior.
        if (newUsers.Count > 0)
            await _db.SaveChangesAsync();

        foreach (var u in newUsers)
        {
            await _db.UserProfiles.AddAsync(new Valour.Database.UserProfile
            {
                Id = u.Id,
                Headline = "Federated account",
                Bio = string.Empty,
                BorderColor = "#fff",
                GlowColor = string.Empty,
                TextColor = string.Empty,
                PrimaryColor = string.Empty,
                SecondaryColor = string.Empty,
                TertiaryColor = string.Empty,
                BackgroundImage = string.Empty,
            });
        }
    }

    /// <summary>
    /// Checks that encryption records belong to the snapshot's channels and
    /// agree with each other. Their signatures are checked by members' apps,
    /// which do not trust the server that relays them.
    /// </summary>
    private static TaskResult ValidateEncryptionRecords(PlanetSnapshot snapshot, HashSet<long> channelIds,
        HashSet<Guid> triggerIds)
    {
        var generations = snapshot.ChannelKeyGenerations;
        if (generations.Any(x => !channelIds.Contains(x.ChannelId) || x.Generation < 1 || x.Body is null ||
                                 x.Signature is null || x.SealPublicKey is null ||
                                 x.IndexGeneration < 1 || x.IndexGeneration > x.Generation ||
                                 (x.HeldSecret is not null && x.HeldSecret.Length != Valour.Sdk.E2ee.E2eeCrypto.KeySize) ||
                                 !DecodesAsGeneration(x, snapshot.Planet.Id)) ||
            generations.GroupBy(x => (x.ChannelId, x.Generation)).Any(x => x.Count() > 1))
            return TaskResult.FromFailure("Snapshot contains invalid channel keys.");

        var latest = generations.GroupBy(x => x.ChannelId).ToDictionary(x => x.Key, x => x.Max(g => g.Generation));
        if (snapshot.Channels.Any(c => c.EncryptionGeneration != latest.GetValueOrDefault(c.Id)) ||
            latest.Any(x => generations.Count(g => g.ChannelId == x.Key) != x.Value))
            return TaskResult.FromFailure("Snapshot channel keys do not match their channels.");

        var generationKeys = generations.Select(x => (x.ChannelId, x.Generation)).ToHashSet();
        if (snapshot.ChannelKeyBoxes.Any(x => !generationKeys.Contains((x.ChannelId, x.Generation)) ||
                                              x.UserId <= 0 || x.Box is null) ||
            snapshot.ChannelKeyBoxes.GroupBy(x => (x.ChannelId, x.Generation, x.UserId)).Any(x => x.Count() > 1))
            return TaskResult.FromFailure("Snapshot contains invalid channel key boxes.");

        if (snapshot.Messages.Any(x => x.EncryptionVersion is not (Valour.Sdk.E2ee.MessageEncryption.None or
                                           Valour.Sdk.E2ee.MessageEncryption.EndToEnd or
                                           Valour.Sdk.E2ee.MessageEncryption.ServerSealed) ||
                                       (x.EncryptionVersion != Valour.Sdk.E2ee.MessageEncryption.None && (x.Envelope is null ||
                                        !generationKeys.Contains((x.ChannelId, x.KeyGeneration))))))
            return TaskResult.FromFailure("Snapshot contains encrypted messages without their channel keys.");

        var log = snapshot.AccessLogEntries;
        if (log.Any(x => x.Body is null || x.Signature is null || x.SignerUserId <= 0 || !DecodesAsAccessLogEntry(x.Body)) ||
            !log.Select(x => x.Seq).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, log.Count)))
            return TaskResult.FromFailure("Snapshot contains an invalid membership log.");

        if (snapshot.AutomodTerms.Any(x => !channelIds.Contains(x.ChannelId) || !triggerIds.Contains(x.TriggerId) ||
                                           x.Terms is null || x.IndexGeneration < 1))
            return TaskResult.FromFailure("Snapshot contains invalid automod terms.");

        if (snapshot.ServerPublicKeys.Any(x => string.IsNullOrEmpty(x.Key) || x.Key.Length > 64 ||
                                               x.Value?.Length != Valour.Sdk.E2ee.E2eeCrypto.KeySize))
            return TaskResult.FromFailure("Snapshot contains invalid server keys.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// True when a key generation's signed body decodes and agrees with the
    /// columns stored beside it. Members check the signature; this keeps a
    /// malformed record from being stored at all.
    /// </summary>
    private static bool DecodesAsGeneration(PlanetSnapshotChannelKeyGeneration generation, long planetId)
    {
        if (generation.Body is null || generation.Body.Length > 4096)
            return false;

        try
        {
            var record = Valour.Sdk.E2ee.ChannelKeyGenerationRecord.Decode(generation.Body);
            return record.ChannelId == generation.ChannelId &&
                   record.PlanetId == planetId &&
                   record.Generation == generation.Generation &&
                   record.CreatorUserId == generation.CreatorUserId &&
                   record.IndexGeneration == generation.IndexGeneration &&
                   record.SealPublicKey is not null && generation.SealPublicKey is not null &&
                   record.SealPublicKey.AsSpan().SequenceEqual(generation.SealPublicKey);
        }
        catch (Valour.Sdk.E2ee.E2eeFormatException)
        {
            return false;
        }
    }

    private static bool DecodesAsAccessLogEntry(byte[] body)
    {
        try
        {
            Valour.Sdk.E2ee.AccessLogRecord.Decode(body);
            return true;
        }
        catch (Valour.Sdk.E2ee.E2eeFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Treat every imported snapshot as untrusted structured input. Even a
    /// hub-originated snapshot crosses a network boundary, and pull-back data is
    /// authored by a community node. Reject cross-planet rows, dangling foreign
    /// keys, duplicate ids, and user ids that appear only in the Users payload.
    /// This prevents an import from becoming a generic arbitrary-row writer.
    /// </summary>
    private static TaskResult ValidateSnapshotGraph(PlanetSnapshot snapshot)
    {
        var planetId = snapshot.Planet.Id;
        if (planetId <= 0 || snapshot.ProtocolVersion != ValourFederation.ProtocolVersion)
            return TaskResult.FromFailure("Snapshot has an invalid planet id or federation protocol version.");

        NormalizeCollections(snapshot);
        if (snapshot.Planet.TagIds.Any(x => x <= 0) ||
            snapshot.Planet.TagIds.Distinct().Count() != snapshot.Planet.TagIds.Count)
        {
            return TaskResult.FromFailure("Snapshot contains invalid or duplicate planet tags.");
        }

        bool InvalidIds<T>(IEnumerable<T> items, Func<T, long> id) =>
            items.Any(x => id(x) <= 0) || items.Select(id).Distinct().Count() != items.Count();

        bool InvalidStringIds<T>(IEnumerable<T> items, Func<T, string> id) =>
            items.Any(x => string.IsNullOrWhiteSpace(id(x))) || items.Select(id).Distinct().Count() != items.Count();

        bool DuplicateUserChannelStates() =>
            snapshot.UserChannelStates
                .GroupBy(x => new { x.UserId, x.ChannelId })
                .Any(x => x.Count() > 1);

        bool HasParentCycle<T>(IEnumerable<T> items, Func<T, long> id, Func<T, long?> parentId)
        {
            var parents = items.ToDictionary(id, parentId);
            foreach (var start in parents.Keys)
            {
                var seen = new HashSet<long>();
                var current = start;
                while (parents.TryGetValue(current, out var parent) && parent.HasValue)
                {
                    if (!seen.Add(current))
                        return true;
                    current = parent.Value;
                }
            }

            return false;
        }

        if (InvalidIds(snapshot.Channels, x => x.Id) || InvalidIds(snapshot.Roles, x => x.Id) ||
            InvalidIds(snapshot.PermissionNodes, x => x.Id) || InvalidIds(snapshot.Members, x => x.Id) ||
            InvalidIds(snapshot.Emojis, x => x.Id) || InvalidIds(snapshot.Rules, x => x.Id) ||
            InvalidIds(snapshot.Bans, x => x.Id) || InvalidIds(snapshot.Messages, x => x.Id) ||
            InvalidIds(snapshot.Attachments, x => x.Id) || InvalidIds(snapshot.Reactions, x => x.Id) ||
            InvalidIds(snapshot.Mentions, x => x.Id) || InvalidIds(snapshot.Threads, x => x.Id) ||
            InvalidIds(snapshot.ThreadComments, x => x.Id) || InvalidIds(snapshot.WikiPages, x => x.Id) ||
            InvalidIds(snapshot.WikiRevisions, x => x.Id) || InvalidIds(snapshot.Users, x => x.Id) ||
            InvalidIds(snapshot.ThreadBoosts, x => x.Id) ||
            InvalidIds(snapshot.ThreadCommentBoosts, x => x.Id) ||
            InvalidIds(snapshot.ModerationAuditLogs, x => x.Id) ||
            snapshot.ThreadBoosts.GroupBy(x => new { x.ThreadId, x.UserId }).Any(x => x.Count() > 1) ||
            snapshot.ThreadCommentBoosts.GroupBy(x => new { x.CommentId, x.UserId }).Any(x => x.Count() > 1) ||
            snapshot.AutomodTriggers.Any(x => x.Id == Guid.Empty) ||
            snapshot.AutomodActions.Any(x => x.Id == Guid.Empty) ||
            snapshot.AutomodLogs.Any(x => x.Id == Guid.Empty) ||
            InvalidStringIds(snapshot.Invites, x => x.Id) ||
            snapshot.UserChannelStates.Any(x => x.UserId <= 0 || x.ChannelId <= 0) ||
            DuplicateUserChannelStates())
        {
            return TaskResult.FromFailure("Snapshot contains missing or duplicate identifiers.");
        }

        if (snapshot.Channels.Any(x => x.PlanetId != planetId) ||
            snapshot.Roles.Any(x => x.PlanetId != planetId || x.FlagBitIndex is < 0 or > 255) ||
            snapshot.PermissionNodes.Any(x => x.PlanetId != planetId) ||
            snapshot.Members.Any(x => x.PlanetId != planetId) ||
            snapshot.Emojis.Any(x => x.PlanetId != planetId) ||
            snapshot.Rules.Any(x => x.PlanetId != planetId) ||
            snapshot.Bans.Any(x => x.PlanetId != planetId) ||
            snapshot.Invites.Any(x => x.PlanetId != planetId) ||
            snapshot.UserChannelStates.Any(x => x.PlanetId != planetId) ||
            snapshot.Messages.Any(x => x.PlanetId != planetId) ||
            snapshot.Threads.Any(x => x.PlanetId != planetId) ||
            snapshot.ThreadComments.Any(x => x.PlanetId != planetId) ||
            snapshot.ThreadBoosts.Any(x => x.PlanetId != planetId) ||
            snapshot.ThreadCommentBoosts.Any(x => x.PlanetId != planetId) ||
            snapshot.WikiPages.Any(x => x.PlanetId != planetId) ||
            snapshot.WikiRevisions.Any(x => x.PlanetId != planetId) ||
            snapshot.AutomodTriggers.Any(x => x.PlanetId != planetId) ||
            snapshot.AutomodActions.Any(x => x.PlanetId != planetId) ||
            snapshot.AutomodLogs.Any(x => x.PlanetId != planetId) ||
            snapshot.ModerationAuditLogs.Any(x => x.PlanetId != planetId))
        {
            return TaskResult.FromFailure("Snapshot contains records for a different planet.");
        }

        var channelIds = snapshot.Channels.Select(x => x.Id).ToHashSet();
        var roleIds = snapshot.Roles.Select(x => x.Id).ToHashSet();
        var memberIds = snapshot.Members.Select(x => x.Id).ToHashSet();
        var memberUserIds = snapshot.Members.ToDictionary(x => x.Id, x => x.UserId);
        var messageIds = snapshot.Messages.Select(x => x.Id).ToHashSet();
        var threadIds = snapshot.Threads.Select(x => x.Id).ToHashSet();
        var threadCommentIds = snapshot.ThreadComments.Select(x => x.Id).ToHashSet();
        var commentThreadIds = snapshot.ThreadComments.ToDictionary(x => x.Id, x => x.ThreadId);
        var wikiPageIds = snapshot.WikiPages.Select(x => x.Id).ToHashSet();
        var triggerIds = snapshot.AutomodTriggers.Select(x => x.Id).ToHashSet();

        if (snapshot.Channels.Any(x => (x.ParentId.HasValue && !channelIds.Contains(x.ParentId.Value)) ||
                                       (x.AssociatedChatChannelId.HasValue && !channelIds.Contains(x.AssociatedChatChannelId.Value))) ||
            snapshot.PermissionNodes.Any(x => !roleIds.Contains(x.RoleId) || !channelIds.Contains(x.TargetId)) ||
            snapshot.UserChannelStates.Any(x => !channelIds.Contains(x.ChannelId) ||
                                               (x.PlanetMemberId.HasValue &&
                                                (!memberIds.Contains(x.PlanetMemberId.Value) ||
                                                 memberUserIds[x.PlanetMemberId.Value] != x.UserId))) ||
            snapshot.Messages.Any(x => !channelIds.Contains(x.ChannelId) ||
                                       (x.AuthorMemberId.HasValue &&
                                        (!memberIds.Contains(x.AuthorMemberId.Value) ||
                                         memberUserIds[x.AuthorMemberId.Value] != x.AuthorUserId)) ||
                                       (x.ReplyToId.HasValue && !messageIds.Contains(x.ReplyToId.Value))) ||
            snapshot.Attachments.Any(x => !messageIds.Contains(x.MessageId)) ||
            snapshot.Reactions.Any(x => !messageIds.Contains(x.MessageId) ||
                                        (x.AuthorMemberId.HasValue &&
                                         (!memberIds.Contains(x.AuthorMemberId.Value) ||
                                          memberUserIds[x.AuthorMemberId.Value] != x.AuthorUserId))) ||
            snapshot.Mentions.Any(x => !messageIds.Contains(x.MessageId) || (x.Type switch
            {
                MentionType.PlanetMember => !memberIds.Contains(x.TargetId),
                MentionType.Channel => !channelIds.Contains(x.TargetId),
                MentionType.Role => !roleIds.Contains(x.TargetId),
                MentionType.User => x.TargetId <= 0,
                _ => true,
            })) ||
            snapshot.Threads.Any(x => x.AuthorMemberId.HasValue &&
                                      (!memberIds.Contains(x.AuthorMemberId.Value) ||
                                       memberUserIds[x.AuthorMemberId.Value] != x.AuthorUserId)) ||
            snapshot.ThreadComments.Any(x => !threadIds.Contains(x.ThreadId) ||
                                             (x.ParentCommentId.HasValue && !snapshot.ThreadComments.Any(c => c.Id == x.ParentCommentId.Value)) ||
                                             (x.ParentCommentId.HasValue && commentThreadIds[x.ParentCommentId.Value] != x.ThreadId) ||
                                             (x.AuthorMemberId.HasValue &&
                                              (!memberIds.Contains(x.AuthorMemberId.Value) ||
                                               memberUserIds[x.AuthorMemberId.Value] != x.AuthorUserId))) ||
            snapshot.ThreadBoosts.Any(x => !threadIds.Contains(x.ThreadId)) ||
            snapshot.ThreadCommentBoosts.Any(x => !threadIds.Contains(x.ThreadId) ||
                                                  !threadCommentIds.Contains(x.CommentId) ||
                                                  commentThreadIds[x.CommentId] != x.ThreadId) ||
            snapshot.WikiPages.Any(x => x.ParentId.HasValue && !wikiPageIds.Contains(x.ParentId.Value)) ||
            snapshot.WikiRevisions.Any(x => !wikiPageIds.Contains(x.PageId)) ||
            (snapshot.Planet.PinnedThreadId.HasValue && !threadIds.Contains(snapshot.Planet.PinnedThreadId.Value)) ||
            snapshot.Members.Any(x => x.DismissedPinThreadId.HasValue && !threadIds.Contains(x.DismissedPinThreadId.Value)) ||
            snapshot.AutomodTriggers.Any(x => !memberIds.Contains(x.MemberAddedBy)) ||
            snapshot.AutomodActions.Any(x => !triggerIds.Contains(x.TriggerId) ||
                                              !memberIds.Contains(x.MemberAddedBy) ||
                                              !memberIds.Contains(x.TargetMemberId) ||
                                              (x.MessageId.HasValue && !messageIds.Contains(x.MessageId.Value)) ||
                                              (x.RoleId.HasValue && !roleIds.Contains(x.RoleId.Value)) ||
                                              (x.ResponseChannelId.HasValue && !channelIds.Contains(x.ResponseChannelId.Value))) ||
            snapshot.AutomodLogs.Any(x => !triggerIds.Contains(x.TriggerId) || !memberIds.Contains(x.MemberId) ||
                                           (x.MessageId.HasValue && !messageIds.Contains(x.MessageId.Value))) ||
            snapshot.ModerationAuditLogs.Any(x =>
                (x.TargetMemberId.HasValue && !memberIds.Contains(x.TargetMemberId.Value)) ||
                (x.MessageId.HasValue && !messageIds.Contains(x.MessageId.Value)) ||
                (x.TriggerId.HasValue && !triggerIds.Contains(x.TriggerId.Value))))
        {
            return TaskResult.FromFailure("Snapshot contains dangling or cross-graph references.");
        }

        var encryptionResult = ValidateEncryptionRecords(snapshot, channelIds, triggerIds);
        if (!encryptionResult.Success)
            return encryptionResult;

        // Category, wiki, and comment trees are traversed recursively in the
        // clients. A cyclic parent chain is valid to a database FK but can hang
        // a renderer or make moderation views unusable, so reject it at the
        // federation boundary before any rows are written.
        if (HasParentCycle(snapshot.Channels, x => x.Id, x => x.ParentId) ||
            HasParentCycle(snapshot.WikiPages, x => x.Id, x => x.ParentId) ||
            HasParentCycle(snapshot.ThreadComments, x => x.Id, x => x.ParentCommentId))
        {
            return TaskResult.FromFailure("Snapshot contains cyclic hierarchy references.");
        }

        if (snapshot.Attachments.Any(x => !string.IsNullOrWhiteSpace(x.CdnBucketItemId)))
            return TaskResult.FromFailure("Snapshot contains storage-backed attachments that are unavailable on this node.");

        var referencedUsers = CollectReferencedUserIds(snapshot);
        var suppliedUsers = snapshot.Users.Select(x => x.Id).ToHashSet();
        if (!referencedUsers.SetEquals(suppliedUsers))
            return TaskResult.FromFailure("Snapshot user records do not exactly match its referenced users.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Every user id a snapshot row points at. Callers normalize null
    /// collections before calling this.
    /// </summary>
    private static HashSet<long> CollectReferencedUserIds(PlanetSnapshot snapshot)
    {
        var referencedUsers = new HashSet<long> { snapshot.Planet.OwnerId };
        referencedUsers.UnionWith(snapshot.Members.Select(x => x.UserId));
        referencedUsers.UnionWith(snapshot.Messages.Select(x => x.AuthorUserId));
        referencedUsers.UnionWith(snapshot.Reactions.Select(x => x.AuthorUserId));
        referencedUsers.UnionWith(snapshot.Emojis.Select(x => x.CreatorUserId));
        referencedUsers.UnionWith(snapshot.Bans.Select(x => x.IssuerId));
        referencedUsers.UnionWith(snapshot.Bans.Select(x => x.TargetId));
        referencedUsers.UnionWith(snapshot.Invites.Select(x => x.IssuerId));
        referencedUsers.UnionWith(snapshot.UserChannelStates.Select(x => x.UserId));
        referencedUsers.UnionWith(snapshot.Threads.Select(x => x.AuthorUserId));
        referencedUsers.UnionWith(snapshot.ThreadComments.Select(x => x.AuthorUserId));
        referencedUsers.UnionWith(snapshot.ThreadBoosts.Select(x => x.UserId));
        referencedUsers.UnionWith(snapshot.ThreadCommentBoosts.Select(x => x.UserId));
        referencedUsers.UnionWith(snapshot.WikiPages.Select(x => x.CreatedByUserId));
        referencedUsers.UnionWith(snapshot.WikiPages.Where(x => x.LastEditedByUserId.HasValue).Select(x => x.LastEditedByUserId!.Value));
        referencedUsers.UnionWith(snapshot.WikiRevisions.Select(x => x.AuthorUserId));
        referencedUsers.UnionWith(snapshot.Mentions.Where(x => x.Type == MentionType.User).Select(x => x.TargetId));
        referencedUsers.UnionWith(snapshot.ModerationAuditLogs.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value));
        referencedUsers.UnionWith(snapshot.ModerationAuditLogs.Where(x => x.TargetUserId.HasValue).Select(x => x.TargetUserId!.Value));
        referencedUsers.UnionWith(EncryptionUserIds(snapshot));
        return referencedUsers;
    }

    private static void NormalizeCollections(PlanetSnapshot snapshot)
    {
        snapshot.Planet.TagIds ??= new();
        snapshot.Channels ??= new();
        snapshot.Roles ??= new();
        snapshot.PermissionNodes ??= new();
        snapshot.Members ??= new();
        snapshot.Emojis ??= new();
        snapshot.Rules ??= new();
        snapshot.Bans ??= new();
        snapshot.Invites ??= new();
        snapshot.UserChannelStates ??= new();
        snapshot.Messages ??= new();
        snapshot.Attachments ??= new();
        snapshot.Reactions ??= new();
        snapshot.Mentions ??= new();
        snapshot.Threads ??= new();
        snapshot.ThreadComments ??= new();
        snapshot.ThreadBoosts ??= new();
        snapshot.ThreadCommentBoosts ??= new();
        snapshot.WikiPages ??= new();
        snapshot.WikiRevisions ??= new();
        snapshot.AutomodTriggers ??= new();
        snapshot.AutomodActions ??= new();
        snapshot.AutomodLogs ??= new();
        snapshot.ModerationAuditLogs ??= new();
        snapshot.Users ??= new();
        snapshot.ChannelKeyGenerations ??= new();
        snapshot.ChannelKeyBoxes ??= new();
        snapshot.AccessLogEntries ??= new();
        snapshot.AutomodTerms ??= new();
        snapshot.ServerPublicKeys ??= new();
    }
}
