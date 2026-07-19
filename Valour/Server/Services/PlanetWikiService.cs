using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Models.Wiki;

namespace Valour.Server.Services;

/// <summary>
/// Business logic for planet docs/wiki trees. Uses plain database queries
/// rather than the HostedPlanet cache: docs can be large, and the public docs
/// pages serve planets with zero online members — anonymous traffic must not
/// force planets into server memory.
/// </summary>
public class PlanetWikiService
{
    public const int ConflictErrorCode = 409;

    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetWikiService> _logger;

    public PlanetWikiService(
        ValourDb db,
        CoreHubService coreHub,
        ILogger<PlanetWikiService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _logger = logger;
    }

    ///////////
    // Reads //
    ///////////

    /// <summary>
    /// Every doc node of the planet (metadata only), ordered by position.
    /// The client assembles the tree from this flat list.
    /// </summary>
    public async Task<List<PlanetWikiPage>> GetTreeAsync(long planetId, bool publishedOnly = false)
    {
        var query = _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId);

        // Folders are structural, so they are never filtered by publish state
        if (publishedOnly)
            query = query.Where(x => x.IsFolder || x.IsPublished);

        return await query
            .OrderBy(x => x.Position)
            .Select(ToModelProjection)
            .ToListAsync();
    }

    public async Task<PlanetWikiPage> GetAsync(long planetId, long pageId) =>
        await _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.Id == pageId)
            .Select(ToModelProjection)
            .FirstOrDefaultAsync();

    public async Task<PlanetWikiPage> GetBySlugAsync(long planetId, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        slug = slug.Trim().ToLowerInvariant();

        return await _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.Slug == slug)
            .Select(ToModelProjection)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Public-page slug resolution: exact slug first, then one-hop rename
    /// redirect via PreviousSlug. When the doc was found by its previous slug,
    /// movedFrom is true and the caller should 301 to the current slug.
    /// </summary>
    public async Task<(PlanetWikiPage Doc, bool MovedFrom)> ResolveSlugAsync(long planetId, string slug)
    {
        var doc = await GetBySlugAsync(planetId, slug);
        if (doc is not null)
            return (doc, false);

        slug = slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
            return (null, false);

        var renamed = await _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.PreviousSlug == slug)
            .Select(ToModelProjection)
            .FirstOrDefaultAsync();

        return (renamed, renamed is not null);
    }

    public async Task<WikiPageContent> GetContentAsync(long planetId, long pageId)
    {
        var data = await _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.Id == pageId && !x.IsFolder)
            .Select(x => new { x.Content, x.Version })
            .FirstOrDefaultAsync();

        if (data is null)
            return null;

        return new WikiPageContent
        {
            PageId = pageId,
            PlanetId = planetId,
            Content = data.Content ?? string.Empty,
            Version = data.Version,
        };
    }

    ////////////
    // Search //
    ////////////

    public async Task<List<WikiSearchResult>> SearchAsync(long planetId, string query, bool includeUnpublished = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<WikiSearchResult>();

        query = query.Trim();
        if (query.Length > 64)
            query = query[..64];

        var search = _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId && !x.IsFolder);

        if (!includeUnpublished)
            search = search.Where(x => x.IsPublished);

        return await search
            .Where(x => x.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("simple", query)))
            .OrderByDescending(x => x.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("simple", query)))
            .Take(25)
            .Select(x => new WikiSearchResult
            {
                PageId = x.Id,
                Title = x.Title,
                Slug = x.Slug,
            })
            .ToListAsync();
    }

    ///////////////
    // Mutations //
    ///////////////

    public async Task<TaskResult<PlanetWikiPage>> CreateAsync(long planetId, WikiPageCreateRequest request, long userId)
    {
        if (request is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Request is required.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetWikiPage>.FromFailure(migrationGuard.Message);

        var title = request.Title?.Trim();
        var titleCheck = ValidateTitle(title);
        if (!titleCheck.Success)
            return TaskResult<PlanetWikiPage>.FromFailure(titleCheck.Message);

        var docCount = await _db.PlanetWikiPages.CountAsync(x => x.PlanetId == planetId);
        if (docCount >= ISharedPlanetWikiPage.MaxPagesPerPlanet)
            return TaskResult<PlanetWikiPage>.FromFailure($"Planets can have at most {ISharedPlanetWikiPage.MaxPagesPerPlanet} docs.");

        var nodes = await GetNodeMapAsync(planetId);

        // Parent + depth validation
        if (request.ParentId is not null)
        {
            if (!nodes.TryGetValue(request.ParentId.Value, out var parent))
                return TaskResult<PlanetWikiPage>.FromFailure("Parent folder not found.");

            if (!parent.IsFolder)
                return TaskResult<PlanetWikiPage>.FromFailure("Parent must be a folder.");

            var parentDepth = GetDepth(nodes, request.ParentId.Value);
            if (parentDepth + 1 >= ISharedPlanetWikiPage.MaxDepth)
                return TaskResult<PlanetWikiPage>.FromFailure($"Docs can be nested at most {ISharedPlanetWikiPage.MaxDepth} levels deep.");
        }

        string slug = null;
        string content = null;

        if (!request.IsFolder)
        {
            content = MarkdownProtections.Sanitize(request.Content ?? string.Empty);
            if (content.Length > ISharedPlanetWikiPage.MaxContentLength)
                return TaskResult<PlanetWikiPage>.FromFailure($"Content must be at most {ISharedPlanetWikiPage.MaxContentLength} characters.");

            var existingSlugs = await _db.PlanetWikiPages
                .Where(x => x.PlanetId == planetId && x.Slug != null)
                .Select(x => x.Slug)
                .ToListAsync();

            var slugResult = ResolveNewSlug(request.Slug, title, existingSlugs);
            if (!slugResult.Success)
                return TaskResult<PlanetWikiPage>.FromFailure(slugResult.Message);

            slug = slugResult.Data;
        }

        var siblingMax = nodes.Values
            .Where(x => x.ParentId == request.ParentId)
            .Select(x => (uint?)x.Position)
            .Max();

        var dbDoc = new Valour.Database.PlanetWikiPage
        {
            Id = IdManager.Generate(),
            PlanetId = planetId,
            ParentId = request.ParentId,
            IsFolder = request.IsFolder,
            Slug = slug,
            Title = title,
            Content = content,
            Position = siblingMax is null ? 0 : siblingMax.Value + 1,
            IsPublished = request.IsPublished,
            Version = 1,
            TimeCreated = DateTime.UtcNow,
            CreatedByUserId = userId,
        };

        try
        {
            await _db.PlanetWikiPages.AddAsync(dbDoc);

            if (!dbDoc.IsFolder)
                await AppendRevisionAsync(dbDoc, userId);

            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create doc on planet {PlanetId}", planetId);
            return TaskResult<PlanetWikiPage>.FromFailure("Failed to create doc.");
        }

        var model = dbDoc.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetWikiPage>.FromData(model);
    }

    /// <summary>
    /// Metadata update: title, slug, and publish state. Structural changes
    /// (parent/position) must go through MoveAsync; content through
    /// SaveContentAsync.
    /// </summary>
    public async Task<TaskResult<PlanetWikiPage>> UpdateAsync(PlanetWikiPage updated, long userId)
    {
        if (updated is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Doc is required.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, updated.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetWikiPage>.FromFailure(migrationGuard.Message);

        var dbDoc = await _db.PlanetWikiPages
            .FirstOrDefaultAsync(x => x.PlanetId == updated.PlanetId && x.Id == updated.Id);

        if (dbDoc is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Doc not found.");

        if (updated.ParentId != dbDoc.ParentId || updated.Position != dbDoc.Position)
            return TaskResult<PlanetWikiPage>.FromFailure("Use the move endpoint to change a doc's location.");

        var title = updated.Title?.Trim();
        var titleCheck = ValidateTitle(title);
        if (!titleCheck.Success)
            return TaskResult<PlanetWikiPage>.FromFailure(titleCheck.Message);

        var titleChanged = dbDoc.Title != title;
        dbDoc.Title = title;

        if (!dbDoc.IsFolder)
        {
            // Slug rename with one-hop redirect memory
            var newSlug = updated.Slug?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(newSlug) && newSlug != dbDoc.Slug)
            {
                var slugCheck = WikiSlugUtils.ValidatePageSlug(newSlug);
                if (!slugCheck.Success)
                    return TaskResult<PlanetWikiPage>.FromFailure(slugCheck.Message);

                var taken = await _db.PlanetWikiPages.AnyAsync(x =>
                    x.PlanetId == dbDoc.PlanetId && x.Slug == newSlug && x.Id != dbDoc.Id);
                if (taken)
                    return TaskResult<PlanetWikiPage>.FromFailure($"The slug '{newSlug}' is already in use.");

                dbDoc.PreviousSlug = dbDoc.Slug;
                dbDoc.Slug = newSlug;
            }

            dbDoc.IsPublished = updated.IsPublished;
        }

        if (titleChanged)
        {
            dbDoc.LastEdited = DateTime.UtcNow;
            dbDoc.LastEditedByUserId = userId;

            if (!dbDoc.IsFolder)
                await AppendRevisionAsync(dbDoc, userId);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update doc {PageId} on planet {PlanetId}", updated.Id, updated.PlanetId);
            return TaskResult<PlanetWikiPage>.FromFailure("Failed to update doc.");
        }

        var model = dbDoc.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetWikiPage>.FromData(model);
    }

    public async Task<TaskResult<PlanetWikiPage>> SaveContentAsync(
        long planetId, long pageId, WikiPageContentUpdateRequest request, long userId)
    {
        if (request is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Request is required.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetWikiPage>.FromFailure(migrationGuard.Message);

        var dbDoc = await _db.PlanetWikiPages
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == pageId);

        if (dbDoc is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Doc not found.");

        if (dbDoc.IsFolder)
            return TaskResult<PlanetWikiPage>.FromFailure("Folders have no content.");

        if (request.BaseVersion != dbDoc.Version)
            return TaskResult<PlanetWikiPage>.FromFailure(
                "This page was updated by someone else since you started editing.",
                ConflictErrorCode);

        var content = MarkdownProtections.Sanitize(request.Content ?? string.Empty);
        if (content.Length > ISharedPlanetWikiPage.MaxContentLength)
            return TaskResult<PlanetWikiPage>.FromFailure($"Content must be at most {ISharedPlanetWikiPage.MaxContentLength} characters.");

        if (content == dbDoc.Content)
            return TaskResult<PlanetWikiPage>.FromData(dbDoc.ToModel());

        dbDoc.Content = content;
        dbDoc.Version += 1;
        dbDoc.LastEdited = DateTime.UtcNow;
        dbDoc.LastEditedByUserId = userId;

        try
        {
            await AppendRevisionAsync(dbDoc, userId);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save content for doc {PageId} on planet {PlanetId}", pageId, planetId);
            return TaskResult<PlanetWikiPage>.FromFailure("Failed to save content.");
        }

        var model = dbDoc.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetWikiPage>.FromData(model);
    }

    public async Task<TaskResult> MoveAsync(long planetId, long pageId, WikiPageMoveRequest request)
    {
        if (request is null)
            return TaskResult.FromFailure("Request is required.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var nodes = await GetNodeMapAsync(planetId);

        if (!nodes.TryGetValue(pageId, out var node))
            return TaskResult.FromFailure("Doc not found.");

        if (request.NewParentId is not null)
        {
            if (!nodes.TryGetValue(request.NewParentId.Value, out var newParent))
                return TaskResult.FromFailure("Destination folder not found.");

            if (!newParent.IsFolder)
                return TaskResult.FromFailure("Destination must be a folder.");

            if (request.NewParentId == pageId)
                return TaskResult.FromFailure("A folder cannot be moved into itself.");

            // Cycle check: walk up from the destination — hitting the moving
            // node means the destination is inside its own subtree
            var cursor = request.NewParentId;
            var guard = 0;
            while (cursor is not null && guard++ <= ISharedPlanetWikiPage.MaxDepth)
            {
                if (cursor == pageId)
                    return TaskResult.FromFailure("A folder cannot be moved into its own subtree.");
                cursor = nodes.TryGetValue(cursor.Value, out var ancestor) ? ancestor.ParentId : null;
            }

            // Depth check for the whole moving subtree
            var newParentDepth = GetDepth(nodes, request.NewParentId.Value);
            var subtreeHeight = GetSubtreeHeight(nodes, pageId);
            if (newParentDepth + 1 + subtreeHeight >= ISharedPlanetWikiPage.MaxDepth)
                return TaskResult.FromFailure($"Docs can be nested at most {ISharedPlanetWikiPage.MaxDepth} levels deep.");
        }

        var oldParentId = node.ParentId;

        // Rebuild sibling orderings in memory, then apply position rewrites
        var newSiblings = nodes.Values
            .Where(x => x.ParentId == request.NewParentId && x.Id != pageId)
            .OrderBy(x => x.Position)
            .Select(x => x.Id)
            .ToList();

        var insertAt = (int)Math.Min(request.NewPosition, (uint)newSiblings.Count);
        newSiblings.Insert(insertAt, pageId);

        var positionUpdates = new Dictionary<long, uint>();
        for (var i = 0; i < newSiblings.Count; i++)
            positionUpdates[newSiblings[i]] = (uint)i;

        if (oldParentId != request.NewParentId)
        {
            var oldSiblings = nodes.Values
                .Where(x => x.ParentId == oldParentId && x.Id != pageId)
                .OrderBy(x => x.Position)
                .ToList();

            for (var i = 0; i < oldSiblings.Count; i++)
                positionUpdates[oldSiblings[i].Id] = (uint)i;
        }

        var changedIds = new List<long>();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var (id, position) in positionUpdates)
            {
                var current = nodes[id];
                var parentChanging = id == pageId && current.ParentId != request.NewParentId;

                if (!parentChanging && current.Position == position)
                    continue;

                if (id == pageId)
                {
                    await _db.PlanetWikiPages
                        .Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.ParentId, request.NewParentId)
                            .SetProperty(p => p.Position, position));
                }
                else
                {
                    await _db.PlanetWikiPages
                        .Where(x => x.Id == id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.Position, position));
                }

                changedIds.Add(id);
            }

            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            _logger.LogError(e, "Failed to move doc {PageId} on planet {PlanetId}", pageId, planetId);
            return TaskResult.FromFailure("Failed to move doc.");
        }

        if (changedIds.Count > 0)
        {
            var changedModels = await _db.PlanetWikiPages.AsNoTracking()
                .Where(x => changedIds.Contains(x.Id))
                .Select(ToModelProjection)
                .ToListAsync();

            foreach (var model in changedModels)
                _coreHub.NotifyPlanetItemChange(model);
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Deletes a doc. Folders delete recursively (database cascade); revisions
    /// go with their pages. Returns the number of removed nodes.
    /// </summary>
    public async Task<TaskResult<int>> DeleteAsync(long planetId, long pageId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<int>.FromFailure(migrationGuard.Message);

        var nodes = await GetNodeMapAsync(planetId);

        if (!nodes.TryGetValue(pageId, out var node))
            return TaskResult<int>.FromFailure("Doc not found.");

        // Collect the whole subtree for delete notifications
        var removedIds = new List<long>();
        var queue = new Queue<long>();
        queue.Enqueue(pageId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            removedIds.Add(current);
            foreach (var child in nodes.Values.Where(x => x.ParentId == current))
                queue.Enqueue(child.Id);
        }

        try
        {
            await _db.PlanetWikiPages.Where(x => x.Id == pageId).ExecuteDeleteAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete doc {PageId} on planet {PlanetId}", pageId, planetId);
            return TaskResult<int>.FromFailure("Failed to delete doc.");
        }

        foreach (var removedId in removedIds)
        {
            if (!nodes.TryGetValue(removedId, out var removed))
                continue;

            _coreHub.NotifyPlanetItemDelete(new PlanetWikiPage
            {
                Id = removed.Id,
                PlanetId = planetId,
                ParentId = removed.ParentId,
                IsFolder = removed.IsFolder,
                Title = removed.Title,
                Slug = removed.Slug,
                Position = removed.Position,
            });
        }

        return TaskResult<int>.FromData(removedIds.Count);
    }

    ///////////////
    // Revisions //
    ///////////////

    public async Task<List<Valour.Shared.Models.Wiki.PlanetWikiRevision>> GetRevisionsAsync(long planetId, long pageId) =>
        await _db.PlanetWikiRevisions.AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.PageId == pageId)
            .OrderByDescending(x => x.TimeCreated)
            .Select(x => new Valour.Shared.Models.Wiki.PlanetWikiRevision
            {
                Id = x.Id,
                PageId = x.PageId,
                PlanetId = x.PlanetId,
                Title = x.Title,
                Content = null,
                AuthorUserId = x.AuthorUserId,
                ImportSource = x.ImportSource,
                TimeCreated = x.TimeCreated,
            })
            .ToListAsync();

    public async Task<Valour.Shared.Models.Wiki.PlanetWikiRevision> GetRevisionAsync(long planetId, long pageId, long revisionId)
    {
        var revision = await _db.PlanetWikiRevisions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.PageId == pageId && x.Id == revisionId);

        return revision.ToModel(includeContent: true);
    }

    public async Task<TaskResult<PlanetWikiPage>> RestoreRevisionAsync(long planetId, long pageId, long revisionId, long userId)
    {
        var dbDoc = await _db.PlanetWikiPages
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == pageId);

        if (dbDoc is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Doc not found.");

        if (dbDoc.IsFolder)
            return TaskResult<PlanetWikiPage>.FromFailure("Folders have no revisions.");

        var revision = await _db.PlanetWikiRevisions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.PageId == pageId && x.Id == revisionId);

        if (revision is null)
            return TaskResult<PlanetWikiPage>.FromFailure("Revision not found.");

        dbDoc.Title = revision.Title;
        dbDoc.Content = revision.Content;
        dbDoc.Version += 1;
        dbDoc.LastEdited = DateTime.UtcNow;
        dbDoc.LastEditedByUserId = userId;

        try
        {
            // The restored state becomes the newest revision, keeping history
            // strictly append-only
            await AppendRevisionAsync(dbDoc, userId);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to restore revision {RevisionId} for doc {PageId}", revisionId, pageId);
            return TaskResult<PlanetWikiPage>.FromFailure("Failed to restore revision.");
        }

        var model = dbDoc.ToModel();
        _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetWikiPage>.FromData(model);
    }

    /////////////
    // Helpers //
    /////////////

    /// <summary>
    /// Server-side projection to the wire model — never pulls the content
    /// column out of the database.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Valour.Database.PlanetWikiPage, PlanetWikiPage>> ToModelProjection =
        x => new PlanetWikiPage
        {
            Id = x.Id,
            PlanetId = x.PlanetId,
            ParentId = x.ParentId,
            IsFolder = x.IsFolder,
            Slug = x.Slug,
            PreviousSlug = x.PreviousSlug,
            Title = x.Title,
            Position = x.Position,
            IsPublished = x.IsPublished,
            Version = x.Version,
            TimeCreated = x.TimeCreated,
            LastEdited = x.LastEdited,
            CreatedByUserId = x.CreatedByUserId,
            LastEditedByUserId = x.LastEditedByUserId,
            ImportSource = x.ImportSource,
        };

    private record struct DocNode(long Id, long? ParentId, bool IsFolder, uint Position, string Title, string Slug);

    /// <summary>
    /// Minimal structural snapshot of a planet's docs, used for parent, depth,
    /// cycle, and sibling calculations without touching content.
    /// </summary>
    private async Task<Dictionary<long, DocNode>> GetNodeMapAsync(long planetId)
    {
        var nodes = await _db.PlanetWikiPages.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Select(x => new DocNode(x.Id, x.ParentId, x.IsFolder, x.Position, x.Title, x.Slug))
            .ToListAsync();

        return nodes.ToDictionary(x => x.Id);
    }

    /// <summary>
    /// Depth of a node (root nodes are depth 0)
    /// </summary>
    private static int GetDepth(Dictionary<long, DocNode> nodes, long id)
    {
        var depth = 0;
        var cursor = nodes.TryGetValue(id, out var node) ? node.ParentId : null;
        while (cursor is not null && depth <= ISharedPlanetWikiPage.MaxDepth)
        {
            depth++;
            cursor = nodes.TryGetValue(cursor.Value, out var parent) ? parent.ParentId : null;
        }

        return depth;
    }

    /// <summary>
    /// Height of the subtree rooted at id (a leaf has height 0)
    /// </summary>
    private static int GetSubtreeHeight(Dictionary<long, DocNode> nodes, long id)
    {
        var height = 0;
        foreach (var child in nodes.Values.Where(x => x.ParentId == id))
            height = Math.Max(height, 1 + GetSubtreeHeight(nodes, child.Id));

        return height;
    }

    private static TaskResult ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return TaskResult.FromFailure("Title cannot be empty.");

        if (title.Length > ISharedPlanetWikiPage.MaxTitleLength)
            return TaskResult.FromFailure($"Title must be at most {ISharedPlanetWikiPage.MaxTitleLength} characters.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Resolves the slug for a new page: explicit slugs must validate and be
    /// free; derived slugs dedupe with a numeric suffix.
    /// </summary>
    private static TaskResult<string> ResolveNewSlug(string requestedSlug, string title, List<string> existingSlugs)
    {
        var taken = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(requestedSlug))
        {
            var slug = requestedSlug.Trim().ToLowerInvariant();

            var check = WikiSlugUtils.ValidatePageSlug(slug);
            if (!check.Success)
                return TaskResult<string>.FromFailure(check.Message);

            if (taken.Contains(slug))
                return TaskResult<string>.FromFailure($"The slug '{slug}' is already in use.");

            return TaskResult<string>.FromData(slug);
        }

        var baseSlug = WikiSlugUtils.Slugify(title);
        if (!taken.Contains(baseSlug))
            return TaskResult<string>.FromData(baseSlug);

        for (var i = 2; i < 1000; i++)
        {
            // Keep the suffixed slug within the length cap
            var suffix = "-" + i;
            var trimmed = baseSlug.Length + suffix.Length > ISharedPlanetWikiPage.MaxSlugLength
                ? baseSlug[..(ISharedPlanetWikiPage.MaxSlugLength - suffix.Length)]
                : baseSlug;

            var candidate = trimmed + suffix;
            if (!taken.Contains(candidate))
                return TaskResult<string>.FromData(candidate);
        }

        return TaskResult<string>.FromFailure("Could not generate a unique slug — set one explicitly.");
    }

    /// <summary>
    /// Appends the doc's current state as a revision and prunes history beyond
    /// the cap. Skips when the newest revision already matches. Saved together
    /// with the caller's SaveChanges.
    /// </summary>
    private async Task AppendRevisionAsync(Valour.Database.PlanetWikiPage dbDoc, long authorUserId)
    {
        var latest = await _db.PlanetWikiRevisions
            .Where(x => x.PageId == dbDoc.Id)
            .OrderByDescending(x => x.TimeCreated)
            .FirstOrDefaultAsync();

        if (latest is not null && latest.Title == dbDoc.Title && latest.Content == dbDoc.Content)
            return;

        await _db.PlanetWikiRevisions.AddAsync(new Valour.Database.PlanetWikiRevision
        {
            Id = IdManager.Generate(),
            PageId = dbDoc.Id,
            PlanetId = dbDoc.PlanetId,
            Title = dbDoc.Title,
            Content = dbDoc.Content ?? string.Empty,
            AuthorUserId = authorUserId,
            TimeCreated = DateTime.UtcNow,
        });

        // Prune the oldest beyond the cap (the one being added counts)
        var count = await _db.PlanetWikiRevisions.CountAsync(x => x.PageId == dbDoc.Id);
        var excess = count + 1 - ISharedPlanetWikiPage.MaxRevisionsPerPage;
        if (excess > 0)
        {
            var oldest = await _db.PlanetWikiRevisions
                .Where(x => x.PageId == dbDoc.Id)
                .OrderBy(x => x.TimeCreated)
                .Take(excess)
                .ToListAsync();

            _db.PlanetWikiRevisions.RemoveRange(oldest);
        }
    }
}
