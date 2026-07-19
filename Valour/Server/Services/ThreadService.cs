using Valour.Server.Cdn;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Models.Threads;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class ThreadService
{
    private static readonly DateTime HotEpoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Seconds of age a thread "earns back" per 10x boosts in the hot ranking
    /// </summary>
    private const double HotBoostWeight = 45000;

    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly AutomodService _automodService;
    private readonly ProxyHandler _proxyHandler;
    private readonly ModerationAuditService _auditService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<ThreadService> _logger;

    public ThreadService(
        ValourDb db,
        CoreHubService coreHub,
        AutomodService automodService,
        ProxyHandler proxyHandler,
        ModerationAuditService auditService,
        NotificationService notificationService,
        ILogger<ThreadService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _automodService = automodService;
        _proxyHandler = proxyHandler;
        _auditService = auditService;
        _notificationService = notificationService;
        _logger = logger;
    }

    ////////////////
    // Thread CRUD //
    ////////////////

    public async Task<PlanetThread> GetThreadAsync(long threadId)
    {
        var thread = await _db.PlanetThreads
            .AsNoTracking()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == threadId);

        return thread.ToModel();
    }

    public async Task<bool> IsThreadsEnabledAsync(long planetId)
    {
        return await _db.Planets
            .AsNoTracking()
            .Where(x => x.Id == planetId)
            .Select(x => x.EnableThreads)
            .FirstOrDefaultAsync();
    }

    public async Task<TaskResult<PlanetThread>> CreateThreadAsync(PlanetThread thread, PlanetMember member)
    {
        var validation = ValidateThread(thread);
        if (!validation.Success)
            return TaskResult<PlanetThread>.FromFailure(validation.Message);

        var migrationGuard = await MigrationLock.GuardAsync(_db, thread.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetThread>.FromFailure(migrationGuard.Message);

        if (!await IsThreadsEnabledAsync(thread.PlanetId))
            return TaskResult<PlanetThread>.FromFailure("Threads are disabled for this planet.");

        thread.Id = IdManager.Generate();
        thread.TimeCreated = DateTime.UtcNow;
        thread.EditedTime = null;
        thread.AuthorUserId = member.UserId;
        thread.AuthorMemberId = member.Id;
        thread.IsLocked = false;
        thread.BoostCount = 0;
        thread.CommentCount = 0;
        // Import provenance is reserved for trusted import workflows.
        thread.ImportSource = null;

        var attachments = thread.Attachments?.Where(x => x is not null).ToList();
        if (attachments is not null)
        {
            foreach (var attachment in attachments)
                attachment.Inline = false;
        }

        if (!string.IsNullOrWhiteSpace(thread.Content))
        {
            thread.Content = SanitizeMarkdown(thread.Content);

            var inlineAttachments = await _proxyHandler.GetUrlAttachmentsFromContent(thread.Content, _db);
            if (inlineAttachments is not null)
            {
                attachments ??= new List<Valour.Sdk.Models.MessageAttachment>();
                attachments.AddRange(inlineAttachments);
            }
        }

        var attachmentResult = await PrepareAttachmentsAsync(thread, attachments);
        if (!attachmentResult.Success)
            return TaskResult<PlanetThread>.FromFailure(attachmentResult.Message);

        var scanResult = await ScanWithAutomodAsync(thread.PlanetId, thread.Id, $"{thread.Title}\n{thread.Content}", thread.AuthorUserId, member);
        if (!scanResult.Success)
            return TaskResult<PlanetThread>.FromFailure(scanResult.Message);

        try
        {
            await _db.PlanetThreads.AddAsync(thread.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create thread on planet {PlanetId}", thread.PlanetId);
            return TaskResult<PlanetThread>.FromFailure("Failed to create thread.");
        }

        _coreHub.NotifyPlanetItemChange(thread);

        return TaskResult<PlanetThread>.FromData(thread);
    }

    public async Task<TaskResult<PlanetThread>> EditThreadAsync(PlanetThread updated)
    {
        var validation = ValidateThread(updated);
        if (!validation.Success)
            return TaskResult<PlanetThread>.FromFailure(validation.Message);

        var migrationGuard = await MigrationLock.GuardAsync(_db, updated.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetThread>.FromFailure(migrationGuard.Message);

        var dbThread = await _db.PlanetThreads
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == updated.Id && x.PlanetId == updated.PlanetId);

        if (dbThread is null)
            return TaskResult<PlanetThread>.FromFailure("Thread not found.");

        updated.Content = SanitizeMarkdown(updated.Content ?? string.Empty);

        dbThread.Title = updated.Title;
        dbThread.Content = updated.Content;
        dbThread.Nsfw = updated.Nsfw;
        dbThread.EditedTime = DateTime.UtcNow;

        // Inline (url preview) attachments are regenerated from the new content;
        // uploaded attachments are immutable after posting.
        var inlineRows = dbThread.Attachments?.Where(x => x.Inline).ToList();
        if (inlineRows is not null && inlineRows.Count > 0)
            _db.ThreadAttachments.RemoveRange(inlineRows);

        var keptCount = dbThread.Attachments?.Count(x => !x.Inline) ?? 0;
        List<Valour.Sdk.Models.MessageAttachment> newInline = null;
        if (!string.IsNullOrWhiteSpace(updated.Content))
        {
            newInline = await _proxyHandler.GetUrlAttachmentsFromContent(updated.Content, _db);
            if (newInline is not null)
            {
                if (keptCount + newInline.Count > ISharedPlanetThread.MaxAttachments)
                    newInline = newInline.Take(Math.Max(0, ISharedPlanetThread.MaxAttachments - keptCount)).ToList();

                var sortOrder = keptCount;
                foreach (var attachment in newInline)
                {
                    attachment.Inline = true;
                    await _db.ThreadAttachments.AddAsync(attachment.ToThreadAttachment(dbThread.Id, sortOrder));
                    sortOrder++;
                }
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to edit thread {ThreadId}", updated.Id);
            return TaskResult<PlanetThread>.FromFailure("Failed to edit thread.");
        }

        var model = await GetThreadAsync(updated.Id);
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetThread>.FromData(model);
    }

    public async Task<TaskResult> DeleteThreadAsync(long planetId, long threadId, long actorUserId, bool isModeration)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var dbThread = await _db.PlanetThreads
            .FirstOrDefaultAsync(x => x.Id == threadId && x.PlanetId == planetId);

        if (dbThread is null)
            return TaskResult.FromFailure("Thread not found.");

        dbThread.IsDeleted = true;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete thread {ThreadId}", threadId);
            return TaskResult.FromFailure("Failed to delete thread.");
        }

        _coreHub.NotifyPlanetItemDelete(dbThread.ToModel());

        if (isModeration)
        {
            await _auditService.LogAsync(
                planetId,
                ModerationActionSource.Manual,
                ModerationActionType.DeleteThread,
                actorUserId: actorUserId,
                targetUserId: dbThread.AuthorUserId,
                targetMemberId: dbThread.AuthorMemberId,
                details: $"Deleted thread \"{dbThread.Title}\" ({threadId})");
        }

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult<PlanetThread>> SetLockedAsync(long planetId, long threadId, bool value, long actorUserId)
    {
        var result = await SetThreadFlagAsync(planetId, threadId, thread => thread.IsLocked = value);

        if (result.Success)
        {
            await _auditService.LogAsync(
                planetId,
                ModerationActionSource.Manual,
                value ? ModerationActionType.LockThread : ModerationActionType.UnlockThread,
                actorUserId: actorUserId,
                targetUserId: result.Data.AuthorUserId,
                targetMemberId: result.Data.AuthorMemberId,
                details: $"{(value ? "Locked" : "Unlocked")} thread \"{result.Data.Title}\" ({threadId})");
        }

        return result;
    }

    /// <summary>
    /// Pins or unpins a thread. There is only ever one pinned thread per planet,
    /// so the pin lives on the planet itself rather than the thread.
    /// </summary>
    public async Task<TaskResult<PlanetThread>> SetPinnedAsync(long planetId, long threadId, bool value, long actorUserId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetThread>.FromFailure(migrationGuard.Message);

        var dbPlanet = await _db.Planets
            .Include(x => x.Tags)
            .FirstOrDefaultAsync(x => x.Id == planetId);
        if (dbPlanet is null)
            return TaskResult<PlanetThread>.FromFailure("Planet not found.");

        var dbThread = await _db.PlanetThreads
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == threadId && x.PlanetId == planetId);

        if (dbThread is null)
            return TaskResult<PlanetThread>.FromFailure("Thread not found.");

        if (value)
            dbPlanet.PinnedThreadId = threadId;
        else if (dbPlanet.PinnedThreadId == threadId)
            dbPlanet.PinnedThreadId = null;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set pin on thread {ThreadId}", threadId);
            return TaskResult<PlanetThread>.FromFailure("Failed to update pin.");
        }

        await _auditService.LogAsync(
            planetId,
            ModerationActionSource.Manual,
            value ? ModerationActionType.PinThread : ModerationActionType.UnpinThread,
            actorUserId: actorUserId,
            targetUserId: dbThread.AuthorUserId,
            targetMemberId: dbThread.AuthorMemberId,
            details: $"{(value ? "Pinned" : "Unpinned")} thread \"{dbThread.Title}\" ({threadId})");

        // Pinning lives on the planet, so broadcast the planet so feeds re-sort live
        _coreHub.NotifyPlanetChange(dbPlanet.ToModel());

        return TaskResult<PlanetThread>.FromData(dbThread.ToModel());
    }

    /// <summary>
    /// Records that a member has dismissed ("marked as read") the given pinned thread,
    /// so it no longer floats to the top of their feed.
    /// </summary>
    public async Task<TaskResult> DismissPinAsync(long planetId, long threadId, long userId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var member = await _db.PlanetMembers
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);

        if (member is null)
            return TaskResult.FromFailure("You are not a member of this planet.");

        member.DismissedPinThreadId = threadId;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to dismiss pin {ThreadId} for user {UserId}", threadId, userId);
            return TaskResult.FromFailure("Failed to dismiss pin.");
        }

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult<PlanetThread>> SetThreadFlagAsync(long planetId, long threadId, Action<Valour.Database.PlanetThread> setFlag)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetThread>.FromFailure(migrationGuard.Message);

        var dbThread = await _db.PlanetThreads
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == threadId && x.PlanetId == planetId);

        if (dbThread is null)
            return TaskResult<PlanetThread>.FromFailure("Thread not found.");

        setFlag(dbThread);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update thread {ThreadId}", threadId);
            return TaskResult<PlanetThread>.FromFailure("Failed to update thread.");
        }

        var model = dbThread.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetThread>.FromData(model);
    }

    //////////
    // Feeds //
    //////////

    public async Task<QueryResponse<PlanetThread>> QueryPlanetThreadsAsync(long planetId, QueryRequest request, long? userId = null)
    {
        var pinnedId = await GetEffectivePinAsync(planetId, userId);

        var query = _db.PlanetThreads
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId);

        query = ApplyFeedSort(query, request, pinnedId);

        return await ExecuteThreadQueryAsync(query, request);
    }

    public async Task<QueryResponse<PlanetThread>> QueryFeedAsync(long userId, QueryRequest request)
    {
        var planetIds = _db.PlanetMembers
            .Where(x => x.UserId == userId)
            .Select(x => x.PlanetId);

        var query = _db.PlanetThreads
            .AsNoTracking()
            .Where(x => planetIds.Contains(x.PlanetId) && x.Planet.EnableThreads);

        // Only Valour Central's pin is allowed to float to the top of the global feed
        var pinnedId = await GetEffectivePinAsync(ISharedPlanet.ValourCentralId, userId);

        query = ApplyFeedSort(query, request, pinnedId);

        return await ExecuteThreadQueryAsync(query, request);
    }

    /// <summary>
    /// Resolves the thread that should float to the top of a planet's feed for the given user,
    /// accounting for the user's per-member dismissal. Returns null when nothing should float.
    /// </summary>
    private async Task<long?> GetEffectivePinAsync(long planetId, long? userId)
    {
        var pinnedId = await _db.Planets
            .AsNoTracking()
            .Where(x => x.Id == planetId)
            .Select(x => x.PinnedThreadId)
            .FirstOrDefaultAsync();

        if (pinnedId is null || userId is null)
            return pinnedId;

        var dismissedId = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.UserId == userId.Value)
            .Select(x => x.DismissedPinThreadId)
            .FirstOrDefaultAsync();

        return dismissedId == pinnedId ? null : pinnedId;
    }

    private static IQueryable<Valour.Database.PlanetThread> ApplyFeedSort(
        IQueryable<Valour.Database.PlanetThread> query,
        QueryRequest request,
        long? pinnedThreadId)
    {
        var sort = request.Options?.Filters?.GetValueOrDefault("sort") ?? "hot";
        var period = request.Options?.Filters?.GetValueOrDefault("period") ?? "all";

        if (sort == "top")
        {
            DateTime? cutoff = period switch
            {
                "day" => DateTime.UtcNow.AddDays(-1),
                "week" => DateTime.UtcNow.AddDays(-7),
                _ => null
            };

            if (cutoff is not null)
                query = query.Where(x => x.TimeCreated >= cutoff);
        }

        IOrderedQueryable<Valour.Database.PlanetThread> ordered;

        if (pinnedThreadId is not null)
        {
            var pinId = pinnedThreadId.Value;
            ordered = query.OrderByDescending(x => x.Id == pinId);
            ordered = sort switch
            {
                "new" => ordered.ThenByDescending(x => x.TimeCreated),
                "top" => ordered.ThenByDescending(x => x.BoostCount).ThenByDescending(x => x.TimeCreated),
                _ => ordered.ThenByDescending(x =>
                    Math.Log10(x.BoostCount + 1) + ((x.TimeCreated - HotEpoch).TotalSeconds / HotBoostWeight))
            };
        }
        else
        {
            ordered = sort switch
            {
                "new" => query.OrderByDescending(x => x.TimeCreated),
                "top" => query.OrderByDescending(x => x.BoostCount).ThenByDescending(x => x.TimeCreated),
                _ => query.OrderByDescending(x =>
                    Math.Log10(x.BoostCount + 1) + ((x.TimeCreated - HotEpoch).TotalSeconds / HotBoostWeight))
            };
        }

        return ordered;
    }

    private static async Task<QueryResponse<PlanetThread>> ExecuteThreadQueryAsync(
        IQueryable<Valour.Database.PlanetThread> query,
        QueryRequest request)
    {
        var take = request.Take;
        if (take > 50 || take <= 0)
            take = 50;

        var total = await query.CountAsync();

        var items = await query
            .Skip(request.Skip)
            .Take(take)
            .Include(x => x.Attachments)
            .ToListAsync();

        return new QueryResponse<PlanetThread>
        {
            Items = items.Select(x => x.ToModel()).ToList(),
            TotalCount = total
        };
    }

    ///////////
    // Boosts //
    ///////////

    public async Task<TaskResult<PlanetThread>> SetThreadBoostAsync(long planetId, long threadId, long userId, bool boosted)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetThread>.FromFailure(migrationGuard.Message);

        var thread = await _db.PlanetThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId && x.PlanetId == planetId);

        if (thread is null)
            return TaskResult<PlanetThread>.FromFailure("Thread not found.");

        var existing = await _db.ThreadBoosts
            .FirstOrDefaultAsync(x => x.ThreadId == threadId && x.UserId == userId);

        try
        {
            if (boosted)
            {
                if (existing is not null)
                    return TaskResult<PlanetThread>.FromData(await GetThreadAsync(threadId));

                await _db.ThreadBoosts.AddAsync(new Valour.Database.ThreadBoost()
                {
                    Id = IdManager.Generate(),
                    ThreadId = threadId,
                    PlanetId = planetId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                await _db.PlanetThreads
                    .Where(x => x.Id == threadId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.BoostCount, x => x.BoostCount + 1));
            }
            else
            {
                if (existing is null)
                    return TaskResult<PlanetThread>.FromData(await GetThreadAsync(threadId));

                _db.ThreadBoosts.Remove(existing);
                await _db.SaveChangesAsync();

                await _db.PlanetThreads
                    .Where(x => x.Id == threadId && x.BoostCount > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.BoostCount, x => x.BoostCount - 1));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set boost on thread {ThreadId}", threadId);
            return TaskResult<PlanetThread>.FromFailure("Failed to update boost.");
        }

        var model = await GetThreadAsync(threadId);
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetThread>.FromData(model);
    }

    public async Task<List<long>> GetBoostedThreadIdsAsync(long userId, List<long> threadIds)
    {
        if (threadIds is null || threadIds.Count == 0)
            return new List<long>();

        return await _db.ThreadBoosts
            .AsNoTracking()
            .Where(x => x.UserId == userId && threadIds.Contains(x.ThreadId))
            .Select(x => x.ThreadId)
            .ToListAsync();
    }

    ///////////////
    // Comments //
    ///////////////

    public async Task<ThreadComment> GetCommentAsync(long commentId)
    {
        var comment = await _db.ThreadComments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId);

        return comment.ToModel();
    }

    public async Task<QueryResponse<ThreadComment>> QueryCommentsAsync(long threadId, QueryRequest request)
    {
        var query = _db.ThreadComments
            .AsNoTracking()
            .Where(x => x.ThreadId == threadId);

        var filters = request.Options?.Filters;

        if (filters?.TryGetValue("parentId", out var parentFilter) == true &&
            long.TryParse(parentFilter, out var parentId))
        {
            query = query.Where(x => x.ParentCommentId == parentId);
        }
        else
        {
            query = query.Where(x => x.ParentCommentId == null);
        }

        var sort = filters?.GetValueOrDefault("sort") ?? "top";

        query = sort switch
        {
            "new" => query.OrderByDescending(x => x.TimeCreated),
            "old" => query.OrderBy(x => x.TimeCreated),
            _ => query.OrderByDescending(x => x.BoostCount).ThenBy(x => x.TimeCreated)
        };

        var take = request.Take;
        if (take > 50 || take <= 0)
            take = 50;

        var total = await query.CountAsync();
        var items = await query
            .Skip(request.Skip)
            .Take(take)
            .ToListAsync();

        return new QueryResponse<ThreadComment>
        {
            Items = items.Select(x => x.ToModel()).ToList(),
            TotalCount = total
        };
    }

    public async Task<TaskResult<ThreadComment>> CreateCommentAsync(ThreadComment comment, PlanetMember member)
    {
        var validation = ValidateComment(comment);
        if (!validation.Success)
            return TaskResult<ThreadComment>.FromFailure(validation.Message);

        var migrationGuard = await MigrationLock.GuardAsync(_db, comment.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<ThreadComment>.FromFailure(migrationGuard.Message);

        if (!await IsThreadsEnabledAsync(comment.PlanetId))
            return TaskResult<ThreadComment>.FromFailure("Threads are disabled for this planet.");

        var dbThread = await _db.PlanetThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == comment.ThreadId && x.PlanetId == comment.PlanetId);

        if (dbThread is null)
            return TaskResult<ThreadComment>.FromFailure("Thread not found.");

        if (dbThread.IsLocked)
            return TaskResult<ThreadComment>.FromFailure("Thread is locked.");

        Valour.Database.ThreadComment parent = null;
        if (comment.ParentCommentId is not null)
        {
            parent = await _db.ThreadComments
                .FirstOrDefaultAsync(x => x.Id == comment.ParentCommentId && x.ThreadId == comment.ThreadId);

            if (parent is null)
                return TaskResult<ThreadComment>.FromFailure("Parent comment not found.");
        }

        comment.Id = IdManager.Generate();
        comment.TimeCreated = DateTime.UtcNow;
        comment.EditedTime = null;
        comment.AuthorUserId = member.UserId;
        comment.AuthorMemberId = member.Id;
        comment.Depth = parent is null ? 0 : Math.Min(parent.Depth + 1, ISharedThreadComment.MaxDepth);
        comment.BoostCount = 0;
        comment.ReplyCount = 0;
        comment.IsDeleted = false;
        // Import provenance is reserved for trusted import workflows.
        comment.ImportSource = null;
        comment.Content = SanitizeMarkdown(comment.Content);

        var scanResult = await ScanWithAutomodAsync(comment.PlanetId, comment.Id, comment.Content, comment.AuthorUserId, member);
        if (!scanResult.Success)
            return TaskResult<ThreadComment>.FromFailure(scanResult.Message);

        try
        {
            await _db.ThreadComments.AddAsync(comment.ToDatabase());

            if (parent is not null)
                parent.ReplyCount += 1;

            await _db.SaveChangesAsync();

            await _db.PlanetThreads
                .Where(x => x.Id == comment.ThreadId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CommentCount, x => x.CommentCount + 1));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create comment on thread {ThreadId}", comment.ThreadId);
            return TaskResult<ThreadComment>.FromFailure("Failed to create comment.");
        }

        _coreHub.NotifyPlanetItemChange(comment);

        if (parent is not null)
            _coreHub.NotifyPlanetItemChange(parent.ToModel());

        var threadModel = await GetThreadAsync(comment.ThreadId);
        if (threadModel is not null)
            _coreHub.NotifyPlanetItemChange(threadModel);

        try
        {
            await _notificationService.HandleThreadCommentAsync(comment, dbThread, parent);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to send notification for comment {CommentId} on thread {ThreadId}", comment.Id, comment.ThreadId);
        }

        return TaskResult<ThreadComment>.FromData(comment);
    }

    public async Task<TaskResult<ThreadComment>> EditCommentAsync(ThreadComment updated)
    {
        var validation = ValidateComment(updated);
        if (!validation.Success)
            return TaskResult<ThreadComment>.FromFailure(validation.Message);

        var migrationGuard = await MigrationLock.GuardAsync(_db, updated.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<ThreadComment>.FromFailure(migrationGuard.Message);

        var dbComment = await _db.ThreadComments
            .FirstOrDefaultAsync(x => x.Id == updated.Id && x.ThreadId == updated.ThreadId);

        if (dbComment is null)
            return TaskResult<ThreadComment>.FromFailure("Comment not found.");

        if (dbComment.IsDeleted)
            return TaskResult<ThreadComment>.FromFailure("Comment was deleted.");

        dbComment.Content = SanitizeMarkdown(updated.Content);
        dbComment.EditedTime = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to edit comment {CommentId}", updated.Id);
            return TaskResult<ThreadComment>.FromFailure("Failed to edit comment.");
        }

        var model = dbComment.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<ThreadComment>.FromData(model);
    }

    public async Task<TaskResult> DeleteCommentAsync(long planetId, long commentId, long actorUserId, bool isModeration)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var dbComment = await _db.ThreadComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PlanetId == planetId);

        if (dbComment is null)
            return TaskResult.FromFailure("Comment not found.");

        // Tombstone to preserve the reply tree
        dbComment.IsDeleted = true;
        dbComment.Content = string.Empty;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete comment {CommentId}", commentId);
            return TaskResult.FromFailure("Failed to delete comment.");
        }

        _coreHub.NotifyPlanetItemChange(dbComment.ToModel());

        if (isModeration)
        {
            await _auditService.LogAsync(
                planetId,
                ModerationActionSource.Manual,
                ModerationActionType.DeleteThreadComment,
                actorUserId: actorUserId,
                targetUserId: dbComment.AuthorUserId,
                targetMemberId: dbComment.AuthorMemberId,
                details: $"Deleted comment {commentId} on thread {dbComment.ThreadId}");
        }

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult<ThreadComment>> SetCommentBoostAsync(long planetId, long commentId, long userId, bool boosted)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<ThreadComment>.FromFailure(migrationGuard.Message);

        var comment = await _db.ThreadComments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PlanetId == planetId);

        if (comment is null)
            return TaskResult<ThreadComment>.FromFailure("Comment not found.");

        var existing = await _db.ThreadCommentBoosts
            .FirstOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userId);

        try
        {
            if (boosted)
            {
                if (existing is not null)
                    return TaskResult<ThreadComment>.FromData(await GetCommentAsync(commentId));

                await _db.ThreadCommentBoosts.AddAsync(new Valour.Database.ThreadCommentBoost()
                {
                    Id = IdManager.Generate(),
                    CommentId = commentId,
                    ThreadId = comment.ThreadId,
                    PlanetId = planetId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                await _db.ThreadComments
                    .Where(x => x.Id == commentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.BoostCount, x => x.BoostCount + 1));
            }
            else
            {
                if (existing is null)
                    return TaskResult<ThreadComment>.FromData(await GetCommentAsync(commentId));

                _db.ThreadCommentBoosts.Remove(existing);
                await _db.SaveChangesAsync();

                await _db.ThreadComments
                    .Where(x => x.Id == commentId && x.BoostCount > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.BoostCount, x => x.BoostCount - 1));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set boost on comment {CommentId}", commentId);
            return TaskResult<ThreadComment>.FromFailure("Failed to update boost.");
        }

        var model = await GetCommentAsync(commentId);
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<ThreadComment>.FromData(model);
    }

    public async Task<List<long>> GetBoostedCommentIdsAsync(long userId, List<long> commentIds)
    {
        if (commentIds is null || commentIds.Count == 0)
            return new List<long>();

        return await _db.ThreadCommentBoosts
            .AsNoTracking()
            .Where(x => x.UserId == userId && commentIds.Contains(x.CommentId))
            .Select(x => x.CommentId)
            .ToListAsync();
    }

    /////////////
    // Helpers //
    /////////////

    private static string SanitizeMarkdown(string content)
        => Utilities.MarkdownProtections.Sanitize(content);

    private async Task<TaskResult> ScanWithAutomodAsync(long planetId, long contentId, string content, long authorUserId, PlanetMember member)
    {
        // Reuses the message automod pipeline by scanning thread content as a synthetic message.
        // Non-blocking automod actions are skipped because they target chat channels.
        var scanMessage = new Message()
        {
            Id = contentId,
            PlanetId = planetId,
            AuthorUserId = authorUserId,
            AuthorMemberId = member?.Id,
            Content = content ?? string.Empty,
            TimeSent = DateTime.UtcNow
        };

        try
        {
            var scanResult = await _automodService.ScanMessageAsync(scanMessage, member);
            if (!scanResult.AllowMessage)
                return TaskResult.FromFailure("Blocked by automod.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to scan thread content with automod");
            return TaskResult.FromFailure("Automod scan failed. Content was not posted.");
        }

        return TaskResult.SuccessResult;
    }

    private static TaskResult ValidateThread(PlanetThread thread)
    {
        if (thread is null)
            return TaskResult.FromFailure("Thread is required.");

        thread.Title = thread.Title?.Trim();
        thread.Content ??= string.Empty;

        if (string.IsNullOrWhiteSpace(thread.Title))
            return TaskResult.FromFailure("Thread title cannot be empty.");

        if (thread.Title.Length > ISharedPlanetThread.MaxTitleLength)
            return TaskResult.FromFailure($"Thread title must be {ISharedPlanetThread.MaxTitleLength} characters or less.");

        if (thread.Content.Length > ISharedPlanetThread.MaxContentLength)
            return TaskResult.FromFailure($"Thread content must be {ISharedPlanetThread.MaxContentLength} characters or less.");

        var uploadedCount = thread.Attachments?.Count(x => x is not null) ?? 0;
        if (uploadedCount > ISharedPlanetThread.MaxAttachments)
            return TaskResult.FromFailure($"Threads support up to {ISharedPlanetThread.MaxAttachments} attachments.");

        return TaskResult.SuccessResult;
    }

    private static TaskResult ValidateComment(ThreadComment comment)
    {
        if (comment is null)
            return TaskResult.FromFailure("Comment is required.");

        if (string.IsNullOrWhiteSpace(comment.Content))
            return TaskResult.FromFailure("Comment cannot be empty.");

        if (comment.Content.Length > ISharedThreadComment.MaxContentLength)
            return TaskResult.FromFailure($"Comment must be {ISharedThreadComment.MaxContentLength} characters or less.");

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> PrepareAttachmentsAsync(
        PlanetThread thread,
        List<Valour.Sdk.Models.MessageAttachment> attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            thread.Attachments = null;
            return TaskResult.SuccessResult;
        }

        if (attachments.Count > ISharedPlanetThread.MaxAttachments)
            attachments = attachments.Take(ISharedPlanetThread.MaxAttachments).ToList();

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];

            if (attachment.Type == MessageAttachmentType.Embed)
                return TaskResult.FromFailure("Embed attachments are not supported on threads.");

            if (attachment.Missing)
            {
                attachment.Location = Valour.Sdk.Models.MessageAttachment.MissingLocation;
            }
            else
            {
                var result = MediaUriHelper.ScanMediaUri(attachment);
                if (!result.Success)
                    return TaskResult.FromFailure(result.Message);
            }

            var bucketResult = await TryAttachCdnBucketItemAsync(attachment);
            if (!bucketResult.Success)
                return bucketResult;

            if (attachment.Id == 0)
                attachment.Id = IdManager.Generate();

            attachment.SortOrder = i;
        }

        thread.Attachments = attachments;
        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> TryAttachCdnBucketItemAsync(Valour.Sdk.Models.MessageAttachment attachment)
    {
        if (attachment.Missing)
            return TaskResult.SuccessResult;

        var bucketItemId = TryParseCdnBucketItemId(attachment.Location);
        if (bucketItemId is null)
            return TaskResult.SuccessResult;

        var bucketItem = await _db.CdnBucketItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == bucketItemId);

        if (bucketItem is null)
            return TaskResult.FromFailure("Attachment was not found.");

        if (bucketItem.SafetyQuarantinedAt is not null)
            return TaskResult.FromFailure("Attachment is not available.");

        attachment.CdnBucketItemId = bucketItem.Id;
        attachment.FileName ??= bucketItem.FileName;
        attachment.MimeType ??= bucketItem.MimeType;

        return TaskResult.SuccessResult;
    }

    private static string TryParseCdnBucketItemId(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return null;

        if (!uri.Host.Equals(ValourHosts.ContentCdnHost, StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 4 ||
            !segments[0].Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{segments[1]}/{segments[2]}/{Uri.UnescapeDataString(segments[3])}";
    }
}
