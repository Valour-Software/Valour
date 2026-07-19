using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Exports a planet's complete data to a portable snapshot and reconstructs it
/// on import. Snapshot ids are the global snowflake ids and are preserved
/// verbatim, so migration keeps every id — no remapping. Referenced users are
/// hub-global; import materializes shadow rows for any the destination lacks.
/// </summary>
public class PlanetSnapshotService
{
    private readonly ValourDb _db;
    private readonly ILogger<PlanetSnapshotService> _logger;

    public PlanetSnapshotService(ValourDb db, ILogger<PlanetSnapshotService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TaskResult<PlanetSnapshot>> ExportAsync(long planetId)
    {
        var planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Planet not found.");

        var channels = await _db.Channels.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var roles = await _db.PlanetRoles.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var permNodes = await _db.PermissionsNodes.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var members = await _db.PlanetMembers.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var emojis = await _db.PlanetEmojis.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var rules = await _db.PlanetRules.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var bans = await _db.PlanetBans.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var messages = await _db.Messages.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();

        var messageIds = messages.Select(x => x.Id).ToHashSet();
        var attachments = await _db.MessageAttachments.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();
        var reactions = await _db.MessageReactions.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();
        var mentions = await _db.MessageMentions.AsNoTracking().Where(x => messageIds.Contains(x.MessageId)).ToListAsync();

        var threads = await _db.PlanetThreads.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var threadComments = await _db.ThreadComments.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var wikiPages = await _db.PlanetWikiPages.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var wikiRevisions = await _db.PlanetWikiRevisions.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var automodTriggers = await _db.AutomodTriggers.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var automodActions = await _db.AutomodActions.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();

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
                EnableThreads = planet.EnableThreads,
                PublicThreads = planet.PublicThreads,
                PinnedThreadId = planet.PinnedThreadId,
                Version = planet.Version,
            },
            Channels = channels.Select(c => new PlanetSnapshotChannel
            {
                Id = c.Id, Name = c.Name, Description = c.Description, ChannelType = c.ChannelType,
                LastUpdateTime = c.LastUpdateTime, PlanetId = c.PlanetId, ParentId = c.ParentId,
                RawPosition = c.RawPosition, InheritsPerms = c.InheritsPerms, IsDefault = c.IsDefault,
                Nsfw = c.Nsfw, AssociatedChatChannelId = c.AssociatedChatChannelId, Version = c.Version,
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
            Messages = messages.Select(m => new PlanetSnapshotMessage
            {
                Id = m.Id, PlanetId = m.PlanetId, ReplyToId = m.ReplyToId, AuthorUserId = m.AuthorUserId,
                AuthorMemberId = m.AuthorMemberId, Content = m.Content, TimeSent = m.TimeSent,
                ChannelId = m.ChannelId, EditedTime = m.EditedTime,
            }).ToList(),
            Attachments = attachments.Select(a => new PlanetSnapshotAttachment
            {
                Id = a.Id, MessageId = a.MessageId, SortOrder = a.SortOrder, Type = a.Type,
                CdnBucketItemId = a.CdnBucketItemId, Location = a.Location, MimeType = a.MimeType,
                FileName = a.FileName, Width = a.Width, Height = a.Height, Inline = a.Inline,
                Missing = a.Missing, Data = a.Data, OpenGraphData = a.OpenGraphData,
                PlanetHosted = a.PlanetHosted, ReportedSha256 = a.ReportedSha256,
            }).ToList(),
            Reactions = reactions.Select(r => new PlanetSnapshotReaction
            {
                Id = r.Id, Emoji = r.Emoji, MessageId = r.MessageId, AuthorUserId = r.AuthorUserId,
                AuthorMemberId = r.AuthorMemberId, CreatedAt = r.CreatedAt,
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
            }).ToList(),
            ThreadComments = threadComments.Select(c => new PlanetSnapshotThreadComment
            {
                Id = c.Id, PlanetId = c.PlanetId, ThreadId = c.ThreadId, ParentCommentId = c.ParentCommentId,
                Depth = c.Depth, AuthorUserId = c.AuthorUserId, AuthorMemberId = c.AuthorMemberId, Content = c.Content,
                TimeCreated = c.TimeCreated, EditedTime = c.EditedTime, BoostCount = c.BoostCount, ReplyCount = c.ReplyCount,
            }).ToList(),
            WikiPages = wikiPages.Select(w => new PlanetSnapshotWikiPage
            {
                Id = w.Id, PlanetId = w.PlanetId, ParentId = w.ParentId, IsFolder = w.IsFolder, Slug = w.Slug,
                PreviousSlug = w.PreviousSlug, Title = w.Title, Content = w.Content, IsPublished = w.IsPublished,
                Version = w.Version, TimeCreated = w.TimeCreated, LastEdited = w.LastEdited,
                CreatedByUserId = w.CreatedByUserId, LastEditedByUserId = w.LastEditedByUserId,
            }).ToList(),
            WikiRevisions = wikiRevisions.Select(r => new PlanetSnapshotWikiRevision
            {
                Id = r.Id, PageId = r.PageId, PlanetId = r.PlanetId, Title = r.Title, Content = r.Content,
                AuthorUserId = r.AuthorUserId, TimeCreated = r.TimeCreated,
            }).ToList(),
            AutomodTriggers = automodTriggers.Select(t => new PlanetSnapshotAutomodTrigger
            {
                Id = t.Id, PlanetId = t.PlanetId, MemberAddedBy = t.MemberAddedBy, Name = t.Name,
                TriggerWords = t.TriggerWords, RunForEveryone = t.RunForEveryone,
            }).ToList(),
            AutomodActions = automodActions.Select(a => new PlanetSnapshotAutomodAction
            {
                Id = a.Id, Strikes = a.Strikes, UseGlobalStrikes = a.UseGlobalStrikes, TriggerId = a.TriggerId,
                MemberAddedBy = a.MemberAddedBy, PlanetId = a.PlanetId, TargetMemberId = a.TargetMemberId,
                MessageId = a.MessageId, RoleId = a.RoleId, Expires = a.Expires, Message = a.Message,
                ResponseChannelId = a.ResponseChannelId,
            }).ToList(),
        };

        // Referenced users (members + message/thread/wiki authors) so the
        // destination can build shadow rows.
        var userIds = members.Select(m => m.UserId)
            .Concat(messages.Select(m => m.AuthorUserId))
            .Concat(threads.Select(t => t.AuthorUserId))
            .Concat(threadComments.Select(c => c.AuthorUserId))
            .Concat(wikiPages.Select(w => w.CreatedByUserId))
            .Concat(wikiRevisions.Select(r => r.AuthorUserId))
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
    /// Reconstructs a planet from a snapshot at its original ids. Fails if the
    /// planet already exists locally (migrate/replace explicitly instead).
    /// </summary>
    public async Task<TaskResult> ImportAsync(PlanetSnapshot snapshot)
    {
        if (snapshot?.Planet is null)
            return TaskResult.FromFailure("Snapshot has no planet.");

        if (await _db.Planets.AnyAsync(x => x.Id == snapshot.Planet.Id))
            return TaskResult.FromFailure("A planet with this id already exists here.");

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            await EnsureShadowUsersAsync(snapshot.Users);

            var p = snapshot.Planet;
            await _db.Planets.AddAsync(new Valour.Database.Planet
            {
                Id = p.Id, OwnerId = p.OwnerId, Name = p.Name, Description = p.Description,
                Public = p.Public, Discoverable = p.Discoverable, Nsfw = p.Nsfw,
                HasCustomIcon = p.HasCustomIcon, HasAnimatedIcon = p.HasAnimatedIcon,
                HasCustomBackground = p.HasCustomBackground, SelfHostedMedia = p.SelfHostedMedia,
                EnableThreads = p.EnableThreads, PublicThreads = p.PublicThreads,
                PinnedThreadId = p.PinnedThreadId, Version = p.Version, IsDeleted = false,
            });

            await _db.PlanetRoles.AddRangeAsync(snapshot.Roles.Select(r => new Valour.Database.PlanetRole
            {
                Id = r.Id, FlagBitIndex = r.FlagBitIndex, IsAdmin = r.IsAdmin, PlanetId = r.PlanetId,
                Position = r.Position, IsDefault = r.IsDefault, Permissions = r.Permissions,
                ChatPermissions = r.ChatPermissions, CategoryPermissions = r.CategoryPermissions,
                VoicePermissions = r.VoicePermissions, Color = r.Color, Bold = r.Bold, Italics = r.Italics,
                Name = r.Name, AnyoneCanMention = r.AnyoneCanMention, Version = r.Version,
            }));

            await _db.Channels.AddRangeAsync(snapshot.Channels.Select(c => new Valour.Database.Channel
            {
                Id = c.Id, Name = c.Name, Description = c.Description, ChannelType = c.ChannelType,
                LastUpdateTime = DateTime.SpecifyKind(c.LastUpdateTime, DateTimeKind.Utc),
                PlanetId = c.PlanetId, ParentId = c.ParentId, RawPosition = c.RawPosition,
                InheritsPerms = c.InheritsPerms, IsDefault = c.IsDefault, Nsfw = c.Nsfw,
                AssociatedChatChannelId = c.AssociatedChatChannelId, Version = c.Version, IsDeleted = false,
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
                Reason = b.Reason, TimeCreated = DateTime.SpecifyKind(b.TimeCreated, DateTimeKind.Utc),
                TimeExpires = b.TimeExpires.HasValue ? DateTime.SpecifyKind(b.TimeExpires.Value, DateTimeKind.Utc) : null,
            }));

            await _db.Messages.AddRangeAsync(snapshot.Messages.Select(m => new Valour.Database.Message
            {
                Id = m.Id, PlanetId = m.PlanetId, ReplyToId = m.ReplyToId, AuthorUserId = m.AuthorUserId,
                AuthorMemberId = m.AuthorMemberId, Content = m.Content,
                TimeSent = DateTime.SpecifyKind(m.TimeSent, DateTimeKind.Utc), ChannelId = m.ChannelId,
                EditedTime = m.EditedTime.HasValue ? DateTime.SpecifyKind(m.EditedTime.Value, DateTimeKind.Utc) : null,
            }));

            await _db.MessageAttachments.AddRangeAsync(snapshot.Attachments.Select(a => new Valour.Database.MessageAttachment
            {
                Id = a.Id, MessageId = a.MessageId, SortOrder = a.SortOrder, Type = a.Type,
                CdnBucketItemId = a.CdnBucketItemId, Location = a.Location, MimeType = a.MimeType,
                FileName = a.FileName, Width = a.Width, Height = a.Height, Inline = a.Inline,
                Missing = a.Missing, Data = a.Data, OpenGraphData = a.OpenGraphData,
                PlanetHosted = a.PlanetHosted, ReportedSha256 = a.ReportedSha256,
            }));

            await _db.MessageReactions.AddRangeAsync(snapshot.Reactions.Select(r => new Valour.Database.MessageReaction
            {
                Id = r.Id, Emoji = r.Emoji, MessageId = r.MessageId, AuthorUserId = r.AuthorUserId,
                AuthorMemberId = r.AuthorMemberId, CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
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
                IsDeleted = false,
            }));

            await _db.ThreadComments.AddRangeAsync((snapshot.ThreadComments ?? new()).Select(c => new Valour.Database.ThreadComment
            {
                Id = c.Id, PlanetId = c.PlanetId, ThreadId = c.ThreadId, ParentCommentId = c.ParentCommentId,
                Depth = c.Depth, AuthorUserId = c.AuthorUserId, AuthorMemberId = c.AuthorMemberId, Content = c.Content,
                TimeCreated = DateTime.SpecifyKind(c.TimeCreated, DateTimeKind.Utc),
                EditedTime = c.EditedTime.HasValue ? DateTime.SpecifyKind(c.EditedTime.Value, DateTimeKind.Utc) : null,
                BoostCount = c.BoostCount, ReplyCount = c.ReplyCount, IsDeleted = false,
            }));

            // Wiki pages before revisions (revisions reference the page).
            await _db.PlanetWikiPages.AddRangeAsync((snapshot.WikiPages ?? new()).Select(w => new Valour.Database.PlanetWikiPage
            {
                Id = w.Id, PlanetId = w.PlanetId, ParentId = w.ParentId, IsFolder = w.IsFolder, Slug = w.Slug,
                PreviousSlug = w.PreviousSlug, Title = w.Title, Content = w.Content, IsPublished = w.IsPublished,
                Version = w.Version, TimeCreated = DateTime.SpecifyKind(w.TimeCreated, DateTimeKind.Utc),
                LastEdited = w.LastEdited.HasValue ? DateTime.SpecifyKind(w.LastEdited.Value, DateTimeKind.Utc) : null,
                CreatedByUserId = w.CreatedByUserId, LastEditedByUserId = w.LastEditedByUserId,
            }));

            await _db.PlanetWikiRevisions.AddRangeAsync((snapshot.WikiRevisions ?? new()).Select(r => new Valour.Database.PlanetWikiRevision
            {
                Id = r.Id, PageId = r.PageId, PlanetId = r.PlanetId, Title = r.Title, Content = r.Content,
                AuthorUserId = r.AuthorUserId, TimeCreated = DateTime.SpecifyKind(r.TimeCreated, DateTimeKind.Utc),
            }));

            // Automod triggers before actions (actions reference the trigger).
            await _db.AutomodTriggers.AddRangeAsync((snapshot.AutomodTriggers ?? new()).Select(t => new Valour.Database.AutomodTrigger
            {
                Id = t.Id, PlanetId = t.PlanetId, MemberAddedBy = t.MemberAddedBy, Name = t.Name,
                TriggerWords = t.TriggerWords, RunForEveryone = t.RunForEveryone,
            }));

            await _db.AutomodActions.AddRangeAsync((snapshot.AutomodActions ?? new()).Select(a => new Valour.Database.AutomodAction
            {
                Id = a.Id, Strikes = a.Strikes, UseGlobalStrikes = a.UseGlobalStrikes, TriggerId = a.TriggerId,
                MemberAddedBy = a.MemberAddedBy, PlanetId = a.PlanetId, TargetMemberId = a.TargetMemberId,
                MessageId = a.MessageId, RoleId = a.RoleId,
                Expires = a.Expires.HasValue ? DateTime.SpecifyKind(a.Expires.Value, DateTimeKind.Utc) : null,
                Message = a.Message, ResponseChannelId = a.ResponseChannelId,
            }));

            await _db.SaveChangesAsync();
            await tran.CommitAsync();

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
    /// Hard-deletes a planet's entire data graph — used for the source side of
    /// a full-handoff migration once the destination has imported the snapshot.
    /// </summary>
    public async Task<TaskResult> DeletePlanetDataAsync(long planetId)
    {
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

            // Economy: reset (Valour-Credits-tied economy never migrates). Transactions
            // reference accounts/currencies, so they go first.
            await _db.Transactions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.EcoAccounts.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Currencies.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();

            await _db.Channels.IgnoreQueryFilters().Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
            await _db.Planets.IgnoreQueryFilters().Where(x => x.Id == planetId).ExecuteDeleteAsync();

            await tran.CommitAsync();
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Failed to delete planet data {PlanetId}", planetId);
            return TaskResult.FromFailure($"Delete failed: {e.Message}");
        }
    }

    private async Task EnsureShadowUsersAsync(List<PlanetSnapshotUser> users)
    {
        if (users is null || users.Count == 0)
            return;

        var ids = users.Select(u => u.Id).ToList();
        var existing = await _db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        var existingSet = existing.ToHashSet();

        foreach (var u in users)
        {
            if (existingSet.Contains(u.Id))
                continue;

            await _db.Users.AddAsync(new Valour.Database.User
            {
                Id = u.Id, Name = u.Name, Tag = u.Tag ?? "0000",
                TimeJoined = DateTime.UtcNow, TimeLastActive = DateTime.UtcNow,
                Compliance = true, IsFederated = true, SubscriptionType = u.SubscriptionType,
            });
            await _db.UserProfiles.AddAsync(new Valour.Database.UserProfile
            {
                Id = u.Id, Headline = "Federated account", BorderColor = "#fff",
            });
        }
    }
}
