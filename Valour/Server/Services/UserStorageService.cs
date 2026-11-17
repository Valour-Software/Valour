using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Valour.Server.Cdn.Storage;
using Valour.Server.Services.GoogleDrive;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Tracks how much Valour-hosted attachment storage each account uses, and
/// runs the cleanup that frees it: previews, a streamed backup archive, and
/// deletion in batches.
/// </summary>
public class UserStorageService
{
    private const int MaxDeleteBatch = 200;
    private static readonly TimeSpan ArchiveTicketLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CompletedArchiveLifetime = TimeSpan.FromHours(6);

    private readonly ValourDb _db;
    private readonly UserAttachmentService _attachments;
    private readonly CdnStorageProvider _storage;
    private readonly DriveSyncService _drive;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserStorageService> _logger;

    public UserStorageService(
        ValourDb db,
        UserAttachmentService attachments,
        CdnStorageProvider storage,
        DriveSyncService drive,
        IMemoryCache cache,
        ILogger<UserStorageService> logger)
    {
        _db = db;
        _attachments = attachments;
        _storage = storage;
        _drive = drive;
        _cache = cache;
        _logger = logger;
    }

    // Usage and quota //

    /// <summary>
    /// The name of the account's active Stargazer tier, or null on the free plan.
    /// </summary>
    public static Task<string> GetSubscriptionTypeAsync(ValourDb db, long userId) =>
        db.UserSubscriptions
            .Where(x => x.UserId == userId && x.Active)
            .Select(x => x.Type)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Bytes on Valour's storage. A file whose Valour copy has expired lives in
    /// the owner's Google Drive and is not counted.
    /// </summary>
    public static async Task<long> GetUsedBytesAsync(ValourDb db, long userId) =>
        await db.CdnBucketItems
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.StoragePurgedAt == null)
            .SumAsync(x => (long)x.SizeBytes);

    /// <summary>
    /// The largest single file the account may upload. A connected Google Drive
    /// raises it to at least <see cref="GoogleDriveLimits.ConnectedMaxUploadBytes"/>,
    /// and a higher subscription limit still wins.
    /// </summary>
    public static async Task<long> GetMaxUploadBytesAsync(ValourDb db, long userId)
    {
        var limit = UserSubscriptionTypes.GetMaxUploadBytes(await GetSubscriptionTypeAsync(db, userId));
        return await UserDriveService.HasActiveConnectionAsync(db, userId)
            ? Math.Max(limit, GoogleDriveLimits.ConnectedMaxUploadBytes)
            : limit;
    }

    /// <summary>
    /// The account's storage allowance in bytes. Accounts with a working Google
    /// Drive are not limited, because their files move to Drive and Valour's
    /// copy expires.
    /// </summary>
    public static async Task<long> GetQuotaBytesAsync(ValourDb db, long userId) =>
        await UserDriveService.HasActiveConnectionAsync(db, userId)
            ? long.MaxValue
            : UserSubscriptionTypes.GetStorageQuotaBytes(await GetSubscriptionTypeAsync(db, userId));

    public static async Task<UserStorageInfo> GetInfoAsync(ValourDb db, long userId)
    {
        var subscription = await GetSubscriptionTypeAsync(db, userId);
        var driveConnected = await UserDriveService.HasActiveConnectionAsync(db, userId);

        return new UserStorageInfo
        {
            UsedBytes = await GetUsedBytesAsync(db, userId),
            // The meter still shows the plan's allowance, which is what a Drive
            // account falls back to if it disconnects.
            QuotaBytes = UserSubscriptionTypes.GetStorageQuotaBytes(subscription),
            MaxUploadBytes = await GetMaxUploadBytesAsync(db, userId),
            SubscriptionType = subscription,
            DriveConnected = driveConnected,
            DriveAvailable = UserDriveService.IsAvailable,
        };
    }

    // Choosing files //

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>
    /// The files a cleanup covers. Files held back for safety review are never
    /// included, because the law requires them to be preserved.
    /// </summary>
    private IQueryable<Valour.Database.CdnBucketItem> BuildQuery(long userId, AttachmentCleanupCriteria criteria)
    {
        var asOf = criteria.AsOf == default ? DateTime.UtcNow : AsUtc(criteria.AsOf);

        var query = _db.CdnBucketItems
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SafetyQuarantinedAt == null && x.CreatedAt <= asOf);

        if (criteria.CreatedBefore is { } before)
        {
            var cutoff = AsUtc(before);
            query = query.Where(x => x.CreatedAt < cutoff);
        }

        if (criteria.Categories is { Count: > 0 })
        {
            var categories = criteria.Categories.Distinct().ToList();
            query = query.Where(x => categories.Contains(x.Category));
        }

        if (criteria.MinSizeBytes is > 0)
        {
            var minimum = criteria.MinSizeBytes.Value;
            query = query.Where(x => x.SizeBytes >= minimum);
        }

        switch (criteria.Scope)
        {
            case AttachmentCleanupScope.Unattached:
                query = query.Where(x =>
                    !_db.MessageAttachments.Any(m => m.CdnBucketItemId == x.Id) &&
                    !_db.ThreadAttachments.Any(t => t.CdnBucketItemId == x.Id));
                break;

            case AttachmentCleanupScope.Selected:
                var planetIds = (criteria.PlanetIds ?? []).Distinct().ToList();
                var directMessages = criteria.IncludeDirectMessages;

                if (planetIds.Count == 0 && !directMessages)
                    return query.Where(x => false);

                query = query.Where(x =>
                    _db.MessageAttachments.Any(m =>
                        m.CdnBucketItemId == x.Id &&
                        ((m.Message.PlanetId != null && planetIds.Contains(m.Message.PlanetId.Value)) ||
                         (directMessages && m.Message.PlanetId == null))) ||
                    _db.ThreadAttachments.Any(t =>
                        t.CdnBucketItemId == x.Id && planetIds.Contains(t.Thread.PlanetId)));
                break;
        }

        return query;
    }

    public async Task<AttachmentCleanupPreview> PreviewAsync(long userId, AttachmentCleanupCriteria criteria)
    {
        var query = BuildQuery(userId, criteria);

        return new AttachmentCleanupPreview
        {
            Count = await query.CountAsync(),
            TotalBytes = await query.SumAsync(x => (long)x.SizeBytes),
            Oldest = await query.MinAsync(x => (DateTime?)x.CreatedAt),
            Newest = await query.MaxAsync(x => (DateTime?)x.CreatedAt),
        };
    }

    /// <summary>
    /// How much storage each planet's attachments use, so a cleanup can pick
    /// places. A file used in several places is counted in each of them.
    /// </summary>
    public async Task<AttachmentPlacesResponse> GetPlacesAsync(long userId)
    {
        var messageRows = await (
            from m in _db.MessageAttachments.AsNoTracking()
            join b in _db.CdnBucketItems.AsNoTracking() on m.CdnBucketItemId equals b.Id
            where b.UserId == userId && b.SafetyQuarantinedAt == null
            select new { PlanetId = m.Message.PlanetId, b.Id, b.SizeBytes })
            .Distinct()
            .ToListAsync();

        var threadRows = await (
            from t in _db.ThreadAttachments.AsNoTracking()
            join b in _db.CdnBucketItems.AsNoTracking() on t.CdnBucketItemId equals b.Id
            where b.UserId == userId && b.SafetyQuarantinedAt == null
            select new { PlanetId = (long?)t.Thread.PlanetId, b.Id, b.SizeBytes })
            .Distinct()
            .ToListAsync();

        var places = messageRows.Concat(threadRows)
            .GroupBy(x => x.PlanetId)
            .Select(g => new AttachmentPlaceUsage
            {
                PlanetId = g.Key,
                Count = g.Select(x => x.Id).Distinct().Count(),
                TotalBytes = g.GroupBy(x => x.Id).Sum(x => (long)x.First().SizeBytes),
            })
            .OrderByDescending(x => x.TotalBytes)
            .ToList();

        var unattached = BuildQuery(userId, new AttachmentCleanupCriteria { Scope = AttachmentCleanupScope.Unattached });

        return new AttachmentPlacesResponse
        {
            Places = places,
            Unattached = new AttachmentPlaceUsage
            {
                Count = await unattached.CountAsync(),
                TotalBytes = await unattached.SumAsync(x => (long)x.SizeBytes),
            },
        };
    }

    // Backup archive //

    private sealed class PendingArchive
    {
        public long UserId { get; init; }
        public AttachmentCleanupCriteria Criteria { get; init; }
        public string CriteriaKey { get; init; }
        public AttachmentArchiveState State { get; set; }
        public int SkippedCount { get; set; }
    }

    private static string CacheKey(string id) => $"attachment-archive:{id}";

    /// <summary>
    /// A stable fingerprint of what a cleanup covers, so a backup is only ever
    /// accepted for the exact files it was made for.
    /// </summary>
    public static string GetCriteriaKey(AttachmentCleanupCriteria criteria)
    {
        var text = string.Join('|',
            criteria.CreatedBefore is { } before ? AsUtc(before).Ticks : 0,
            (criteria.AsOf == default ? DateTime.UtcNow : AsUtc(criteria.AsOf)).Ticks,
            (int)criteria.Scope,
            string.Join(',', (criteria.PlanetIds ?? []).Distinct().Order()),
            criteria.IncludeDirectMessages,
            string.Join(',', (criteria.Categories ?? []).Distinct().Order()),
            criteria.MinSizeBytes ?? 0);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public async Task<AttachmentArchiveTicket> CreateArchiveTicketAsync(long userId, AttachmentCleanupCriteria criteria)
    {
        // Fix the cutoff now, so the archive, the delete and the preview all
        // look at the same files even if the client left it unset.
        criteria.AsOf = criteria.AsOf == default ? DateTime.UtcNow : AsUtc(criteria.AsOf);

        var preview = await PreviewAsync(userId, criteria);
        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        _cache.Set(CacheKey(id), new PendingArchive
        {
            UserId = userId,
            Criteria = criteria,
            CriteriaKey = GetCriteriaKey(criteria),
            State = AttachmentArchiveState.Pending,
        }, ArchiveTicketLifetime);

        return new AttachmentArchiveTicket
        {
            Id = id,
            DownloadPath = $"api/attachments/cleanup/archive/{id}",
            Count = preview.Count,
            TotalBytes = preview.TotalBytes,
        };
    }

    public AttachmentArchiveStatus GetArchiveStatus(long userId, string id)
    {
        if (id is null || !_cache.TryGetValue(CacheKey(id), out PendingArchive archive) || archive.UserId != userId)
            return new AttachmentArchiveStatus { State = AttachmentArchiveState.Expired };

        return new AttachmentArchiveStatus { State = archive.State, SkippedCount = archive.SkippedCount };
    }

    /// <summary>
    /// Streams the zip to the caller. Nothing is built in memory or on disk,
    /// and the archive only counts as downloaded once the last byte is written.
    /// The id works once, and it is the only credential, so the browser can
    /// download it directly.
    /// </summary>
    public async Task<TaskResult> StreamArchiveAsync(string id, Stream output, CancellationToken cancellationToken)
    {
        if (id is null || !_cache.TryGetValue(CacheKey(id), out PendingArchive archive))
            return TaskResult.FromFailure("This download has expired. Start it again.", 404);

        lock (archive)
        {
            if (archive.State != AttachmentArchiveState.Pending)
                return TaskResult.FromFailure("This download was already started. Start it again.", 409);

            archive.State = AttachmentArchiveState.Streaming;
        }

        try
        {
            var items = await BuildQuery(archive.UserId, archive.Criteria)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            var manifest = new List<object>(items.Count);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The response body only accepts asynchronous writes, so the archive is
            // written and finished asynchronously too.
            await using (var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null))
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryName = UniqueEntryName(item, usedNames);
                    var written = await TryWriteEntryAsync(zip, entryName, item, cancellationToken);
                    if (!written)
                        archive.SkippedCount++;

                    manifest.Add(new
                    {
                        file = entryName,
                        originalName = item.FileName,
                        mimeType = item.MimeType,
                        sizeBytes = item.SizeBytes,
                        uploadedAt = AsUtc(item.CreatedAt),
                        included = written,
                    });
                }

                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var manifestStream = await manifestEntry.OpenAsync(cancellationToken))
                {
                    await JsonSerializer.SerializeAsync(manifestStream, manifest,
                        new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
                }
            }

            await output.FlushAsync(cancellationToken);

            archive.State = AttachmentArchiveState.Completed;
            _cache.Set(CacheKey(id), archive, CompletedArchiveLifetime);
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            archive.State = AttachmentArchiveState.Failed;
            _logger.LogWarning(e, "Attachment archive {ArchiveId} did not finish", id);
            return TaskResult.FromFailure("The archive could not be completed.");
        }
    }

    private async Task<bool> TryWriteEntryAsync(
        ZipArchive zip, string entryName, Valour.Database.CdnBucketItem item, CancellationToken cancellationToken)
    {
        // A file whose Valour copy has expired is read back from the owner's Drive.
        if (item.StoragePurgedAt is not null && !await _drive.RestoreAsync(item))
        {
            _logger.LogWarning("Attachment {ItemId} could not be read from Google Drive for the backup archive", item.Id);
            return false;
        }

        // Storage reports both a missing file and a failed read as nothing, so
        // try a few times before treating the file as gone.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var download = await _storage.Private.GetAsync(item.Hash);
            if (download is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                continue;
            }

            // Uploads are mostly images, video and audio that are already
            // compressed, so storing them as they are keeps this fast.
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(AsUtc(item.CreatedAt));
            await using var entryStream = await entry.OpenAsync(cancellationToken);
            await download.Stream.CopyToAsync(entryStream, cancellationToken);
            return true;
        }

        _logger.LogWarning("Attachment {ItemId} could not be read for the backup archive", item.Id);
        return false;
    }

    private static string UniqueEntryName(Valour.Database.CdnBucketItem item, HashSet<string> used)
    {
        var folder = $"{item.Category}/{AsUtc(item.CreatedAt):yyyy-MM}";
        var fileName = SanitizeFileName(item.FileName);
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        var name = $"{folder}/{fileName}";
        for (var n = 2; !used.Add(name); n++)
            name = $"{folder}/{stem} ({n}){extension}";

        return name;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((fileName ?? string.Empty).Select(c => invalid.Contains(c) || c is '\\' or '/' ? '_' : c).ToArray()).Trim('.', ' ');
        if (cleaned.Length > 120)
            cleaned = cleaned[..120];
        return cleaned.Length == 0 ? "file" : cleaned;
    }

    // Deleting //

    /// <summary>
    /// Deletes the next batch of matching files. The caller repeats until
    /// nothing remains, which keeps each request short and lets a cleanup that
    /// was interrupted carry on. Unless the user chose to skip it, a backup of
    /// exactly these files must already have been downloaded in full.
    /// </summary>
    public async Task<TaskResult<AttachmentCleanupDeleteResult>> DeleteBatchAsync(
        long userId, AttachmentCleanupDeleteRequest request)
    {
        if (request?.Criteria is null)
            return TaskResult<AttachmentCleanupDeleteResult>.FromFailure("Include the cleanup criteria.");

        if (!request.SkipBackup)
        {
            var status = GetArchiveStatus(userId, request.ArchiveId);
            if (status.State != AttachmentArchiveState.Completed ||
                !_cache.TryGetValue(CacheKey(request.ArchiveId), out PendingArchive archive) ||
                archive.CriteriaKey != GetCriteriaKey(request.Criteria))
            {
                return TaskResult<AttachmentCleanupDeleteResult>.FromFailure(
                    "Download the backup for these files before deleting them.", 409);
            }
        }

        var take = Math.Clamp(request.MaxItems, 1, MaxDeleteBatch);
        var batch = await BuildQuery(userId, request.Criteria)
            .OrderBy(x => x.CreatedAt)
            .Take(take)
            .Select(x => new { x.Category, x.Hash, x.SizeBytes })
            .ToListAsync();

        var result = new AttachmentCleanupDeleteResult();
        foreach (var item in batch)
        {
            var deleted = await _attachments.DeleteAsync(userId, item.Category, item.Hash);
            if (deleted.Success)
            {
                result.Deleted++;
                result.FreedBytes += item.SizeBytes;
            }
        }

        result.Remaining = await BuildQuery(userId, request.Criteria).CountAsync();

        // A batch where nothing could be deleted would otherwise be repeated forever.
        if (batch.Count > 0 && result.Deleted == 0)
            return TaskResult<AttachmentCleanupDeleteResult>.FromFailure("Some attachments could not be deleted.");

        return TaskResult<AttachmentCleanupDeleteResult>.FromData(result);
    }
}
