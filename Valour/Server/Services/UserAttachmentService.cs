using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class UserAttachmentService
{
    private const int MaxTake = 100;
    private const string MissingFileName = "Attachment not found";

    private readonly ValourDb _db;
    private readonly CdnBucketService _bucketService;
    private readonly CdnMemoryCache _cdnCache;
    private readonly ChatCacheService _chatCacheService;
    private readonly CoreHubService _coreHubService;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ILogger<UserAttachmentService> _logger;

    public UserAttachmentService(
        ValourDb db,
        CdnBucketService bucketService,
        CdnMemoryCache cdnCache,
        ChatCacheService chatCacheService,
        CoreHubService coreHubService,
        NodeLifecycleService nodeLifecycleService,
        ILogger<UserAttachmentService> logger)
    {
        _db = db;
        _bucketService = bucketService;
        _cdnCache = cdnCache;
        _chatCacheService = chatCacheService;
        _coreHubService = coreHubService;
        _nodeLifecycleService = nodeLifecycleService;
        _logger = logger;
    }

    public async Task<QueryResponse<UserAttachmentInfo>> QueryAsync(long userId, QueryRequest request)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 50 : Math.Min(request.Take, MaxTake);
        var options = request.Options;

        var query = _db.CdnBucketItems
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (options?.Filters is not null &&
            options.Filters.TryGetValue("search", out var search) &&
            !string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.FileName, $"%{normalized}%") ||
                EF.Functions.ILike(x.MimeType, $"%{normalized}%") ||
                EF.Functions.ILike(x.Hash, $"%{normalized}%"));
        }

        var totalCount = await query.CountAsync();

        query = ApplySort(query, options?.Sort);

        var dbItems = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var items = dbItems.Select(x => x.ToInfo()).ToList();
        await AddSignedUrlsAsync(items, dbItems);

        return new QueryResponse<UserAttachmentInfo>
        {
            Items = items,
            TotalCount = totalCount
        };
    }

    private async Task AddSignedUrlsAsync(
        List<UserAttachmentInfo> items,
        List<Valour.Database.CdnBucketItem> dbItems)
    {
        var byId = items.ToDictionary(x => x.Id);
        var signedUrls = await Task.WhenAll(dbItems.Select(async dbItem =>
        {
            var signedUrl = await ContentApi.GetSignedUrlAsync(_cdnCache, dbItem);
            return (dbItem.Id, signedUrl);
        }));

        foreach (var (id, signedUrl) in signedUrls)
        {
            if (byId.TryGetValue(id, out var item))
                item.SignedUrl = signedUrl;
        }
    }

    public async Task<TaskResult> DeleteAsync(long userId, ContentCategory category, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return TaskResult.FromFailure("Include attachment hash.");

        var id = $"{category}/{userId}/{hash}";
        var item = await _db.CdnBucketItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
            return TaskResult.FromFailure("Attachment not found.", 404);

        var affectedMessageIds = new List<long>();
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var messageAttachments = await _db.MessageAttachments
                .Where(x => x.CdnBucketItemId == id)
                .ToListAsync();
            affectedMessageIds = messageAttachments
                .Select(x => x.MessageId)
                .Distinct()
                .ToList();

            foreach (var attachment in messageAttachments)
            {
                MarkMissing(attachment);
            }

            _db.CdnBucketItems.Remove(item);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            _logger.LogError(e, "Failed to delete attachment {AttachmentId}", id);
            return TaskResult.FromFailure("Failed to delete attachment.");
        }

        _chatCacheService.MarkAttachmentMissing(id, MissingFileName);
        var changedStagedMessages = PlanetMessageWorker.MarkAttachmentMissing(id, MissingFileName);
        try
        {
            await RelayAffectedMessagesAsync(affectedMessageIds, changedStagedMessages);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Attachment row {AttachmentId} was detached, but realtime message updates failed.", id);
        }

        var deleteObjectResult = await _bucketService.DeletePrivateObjectIfUnusedAsync(hash, _db);
        if (!deleteObjectResult.Success)
        {
            _logger.LogWarning("Attachment row {AttachmentId} was detached, but object cleanup failed: {Message}",
                id,
                deleteObjectResult.Message);
        }

        return TaskResult.SuccessResult;
    }

    private async Task RelayAffectedMessagesAsync(
        List<long> messageIds,
        List<Message> stagedMessages)
    {
        if (messageIds.Count == 0 && stagedMessages.Count == 0)
            return;

        var persistedMessages = await _db.Messages
            .AsNoTracking()
            .Include(x => x.ReplyToMessage)
                .ThenInclude(x => x.Attachments)
            .Include(x => x.ReplyToMessage)
                .ThenInclude(x => x.Mentions)
            .Include(x => x.Reactions)
            .Include(x => x.Attachments)
            .Include(x => x.Mentions)
            .Where(x => messageIds.Contains(x.Id))
            .Select(x => x.ToModel())
            .ToListAsync();

        var messages = persistedMessages
            .Concat(stagedMessages)
            .DistinctBy(x => x.Id)
            .ToList();

        var directChannelMembers = new Dictionary<long, List<long>>();

        foreach (var message in messages)
        {
            _chatCacheService.ReplaceMessage(message);

            if (message.PlanetId is not null)
            {
                _coreHubService.RelayMessageEdit(message);
                continue;
            }

            if (!directChannelMembers.TryGetValue(message.ChannelId, out var userIds))
            {
                userIds = await _db.ChannelMembers
                    .AsNoTracking()
                    .Where(x => x.ChannelId == message.ChannelId)
                    .Select(x => x.UserId)
                    .ToListAsync();
                directChannelMembers[message.ChannelId] = userIds;
            }

            await _coreHubService.RelayDirectMessageEdit(message, _nodeLifecycleService, userIds);
        }
    }

    private static IQueryable<Valour.Database.CdnBucketItem> ApplySort(
        IQueryable<Valour.Database.CdnBucketItem> query,
        QuerySort sort)
    {
        var descending = sort?.Descending ?? true;
        var field = sort?.Field?.Trim();

        return field switch
        {
            "fileName" => descending
                ? query.OrderByDescending(x => x.FileName).ThenByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.FileName).ThenByDescending(x => x.CreatedAt),
            "sizeBytes" => descending
                ? query.OrderByDescending(x => x.SizeBytes).ThenByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.SizeBytes).ThenByDescending(x => x.CreatedAt),
            "mimeType" => descending
                ? query.OrderByDescending(x => x.MimeType).ThenByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.MimeType).ThenByDescending(x => x.CreatedAt),
            "category" => descending
                ? query.OrderByDescending(x => x.Category).ThenByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.Category).ThenByDescending(x => x.CreatedAt),
            _ => descending
                ? query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };
    }

    private static void MarkMissing(Valour.Database.MessageAttachment attachment)
    {
        attachment.CdnBucketItemId = null;
        attachment.Location = Valour.Sdk.Models.MessageAttachment.MissingLocation;
        attachment.Type = MessageAttachmentType.File;
        attachment.MimeType = "application/octet-stream";
        attachment.FileName = MissingFileName;
        attachment.Width = 0;
        attachment.Height = 0;
        attachment.Inline = false;
        attachment.Missing = true;
        attachment.Data = null;
        attachment.OpenGraphData = null;
    }
}
