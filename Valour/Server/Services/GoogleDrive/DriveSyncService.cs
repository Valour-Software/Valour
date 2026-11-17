using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Valour.Config.Configs;
using Valour.Server.Cdn.Storage;
using Valour.Server.Mapping;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Server.Services.GoogleDrive;

/// <summary>
/// Copies attachments to their owners' Google Drives, removes Valour's own copy
/// once it has expired, and brings a file back from Drive when someone opens it
/// after that. The worker calls <see cref="RunPassAsync"/> on a timer.
///
/// Drive is where a file lives. Valour's copy is a cache that lasts
/// <see cref="GoogleDriveConfig.RetentionDays"/> days.
/// </summary>
public class DriveSyncService
{
    private const int MaxFolderNameLength = 100;
    private static readonly TimeSpan SyncLease = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan FolderCheckLifetime = TimeSpan.FromMinutes(10);

    /// <summary>One read from Drive per file at a time, so a burst of views does not multiply downloads.</summary>
    private static readonly ConcurrentDictionary<string, Task<bool>> RestoresInFlight = new(StringComparer.Ordinal);

    /// <summary>Files are held in memory while they are read back, so only a few are read at once.</summary>
    private static readonly SemaphoreSlim RestoreGate = new(3);

    private readonly ValourDb _db;
    private readonly UserDriveService _drives;
    private readonly IGoogleDriveApi _api;
    private readonly CdnStorageProvider _storage;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DriveSyncService> _logger;

    public DriveSyncService(
        ValourDb db,
        UserDriveService drives,
        IGoogleDriveApi api,
        CdnStorageProvider storage,
        IMemoryCache cache,
        ILogger<DriveSyncService> logger)
    {
        _db = db;
        _drives = drives;
        _api = api;
        _storage = storage;
        _cache = cache;
        _logger = logger;
    }

    private static GoogleDriveConfig Config => GoogleDriveConfig.Current;

    // One worker pass //

    /// <summary>Copies a batch of waiting files and removes expired copies. Returns the files copied.</summary>
    public async Task<int> RunPassAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseStaleClaimsAsync();
        var copied = await SyncBatchAsync(cancellationToken);
        await PurgeExpiredAsync(cancellationToken);
        return copied;
    }

    /// <summary>
    /// A worker that stopped partway through a file leaves it marked as being
    /// copied. After the lease runs out another pass takes it over.
    /// </summary>
    private Task ReleaseStaleClaimsAsync()
    {
        var now = DateTime.UtcNow;
        return _db.CdnBucketItems
            .Where(x => x.DriveState == DriveSyncState.Syncing && x.DriveNextAttemptAt < now)
            .ExecuteUpdateAsync(x => x
                .SetProperty(i => i.DriveState, DriveSyncState.Pending)
                .SetProperty(i => i.DriveNextAttemptAt, (DateTime?)null));
    }

    // Copying to Drive //

    private record Candidate(string Id, long UserId);

    public async Task<int> SyncBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var graceCutoff = now.AddMinutes(-Config.AttachGraceMinutes);

        // Files wait for Drive when queued by hand (Pending), and new uploads
        // (None, made after the connection) are picked up on their own. A new
        // file waits a little so it has been attached to a message by then and
        // can be filed under that planet or conversation.
        var candidates = await (
            from c in _db.UserDriveConnections.AsNoTracking()
            where c.Status == DriveConnectionStatus.Active
            from i in _db.CdnBucketItems
            where i.UserId == c.UserId &&
                  i.SafetyQuarantinedAt == null &&
                  i.StoragePurgedAt == null &&
                  i.DriveFileId == null &&
                  (i.DriveNextAttemptAt == null || i.DriveNextAttemptAt <= now) &&
                  ((i.DriveState == DriveSyncState.Pending) ||
                   (i.DriveState == DriveSyncState.None && c.AutoSyncFrom != null && i.CreatedAt >= c.AutoSyncFrom && i.CreatedAt <= graceCutoff))
            orderby i.CreatedAt
            select new Candidate(i.Id, i.UserId))
            .Take(Config.SyncBatchSize)
            .ToListAsync(cancellationToken);

        var copied = 0;
        var blockedUsers = new HashSet<long>();

        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (blockedUsers.Contains(candidate.UserId))
                continue;

            // Only one worker takes a file.
            var lease = DateTime.UtcNow + SyncLease;
            var claimed = await _db.CdnBucketItems
                .Where(x => x.Id == candidate.Id &&
                            (x.DriveState == DriveSyncState.Pending || x.DriveState == DriveSyncState.None))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(i => i.DriveState, DriveSyncState.Syncing)
                    .SetProperty(i => i.DriveNextAttemptAt, lease), cancellationToken);
            if (claimed == 0)
                continue;

            var outcome = await SyncItemAsync(candidate.Id, cancellationToken);
            if (outcome == SyncOutcome.Copied)
                copied++;
            else if (outcome == SyncOutcome.StopForUser)
                blockedUsers.Add(candidate.UserId);
        }

        return copied;
    }

    private enum SyncOutcome
    {
        Copied,
        Retry,
        Failed,

        /// <summary>The account's Drive can't take more files right now, so its other files wait too.</summary>
        StopForUser,
    }

    private async Task<SyncOutcome> SyncItemAsync(string itemId, CancellationToken cancellationToken)
    {
        // The claim changed the row without the tracker knowing.
        _db.ChangeTracker.Clear();
        var item = await _db.CdnBucketItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null)
            return SyncOutcome.Failed;

        try
        {
            var token = await _drives.GetAccessTokenAsync(item.UserId, cancellationToken);

            await using var source = await _storage.Private.GetAsync(item.Hash);
            if (source is null)
                return await FailAsync(item, "The file is missing from storage.", permanent: true);

            var placement = await ResolvePlacementAsync(item);

            string driveId;
            try
            {
                driveId = await UploadToPlacementAsync(item, token, placement, source.Stream, cancellationToken);
            }
            catch (DriveApiException e) when (e.Kind == DriveErrorKind.NotFound)
            {
                // The person deleted or trashed a folder. Valour makes it again.
                await ForgetMissingFoldersAsync(item.UserId, token, placement, cancellationToken);

                await using var retrySource = await _storage.Private.GetAsync(item.Hash);
                if (retrySource is null)
                    return await FailAsync(item, "The file is missing from storage.", permanent: true);

                driveId = await UploadToPlacementAsync(item, token, placement, retrySource.Stream, cancellationToken);
            }

            item.DriveState = DriveSyncState.Synced;
            item.DriveFileId = driveId;
            item.DriveSyncedAt = DateTime.UtcNow;
            item.DriveAttempts = 0;
            item.DriveNextAttemptAt = null;
            item.DriveError = null;
            // A file the person sent to Drive by hand loses its Valour copy right away.
            item.StorageExpiresAt = item.DriveReleaseOnSync ? DateTime.UtcNow : DateTime.UtcNow.AddDays(Config.RetentionDays);
            item.DriveReleaseOnSync = false;
            await _db.SaveChangesAsync(cancellationToken);

            await _db.UserDriveConnections
                .Where(x => x.UserId == item.UserId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(c => c.LastSyncAt, DateTime.UtcNow)
                    .SetProperty(c => c.LastError, (string)null), cancellationToken);

            return SyncOutcome.Copied;
        }
        catch (DriveApiException e) when (e.Kind == DriveErrorKind.AccessRevoked)
        {
            // The connection is marked as needing a reconnect. The file waits.
            await ReleaseAsync(item, null);
            return SyncOutcome.StopForUser;
        }
        catch (DriveApiException e) when (e.Kind == DriveErrorKind.DriveFull)
        {
            await _drives.SetLastErrorAsync(item.UserId, "Your Google Drive is full. Free up space and files will keep copying.");
            await ReleaseAsync(item, DateTime.UtcNow.AddHours(1));
            return SyncOutcome.StopForUser;
        }
        catch (DriveApiException e) when (e.Kind == DriveErrorKind.Transient)
        {
            return await FailAsync(item, e.Message, permanent: false);
        }
        catch (DriveApiException e)
        {
            _logger.LogWarning(e, "Copying {ItemId} to Google Drive failed", item.Id);
            return await FailAsync(item, e.Message, permanent: false);
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(item, null);
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Copying {ItemId} to Google Drive failed unexpectedly", item.Id);
            return await FailAsync(item, "Something went wrong copying this file.", permanent: false);
        }
    }

    /// <summary>Puts a file back in the queue to try again later, without counting it as a failure.</summary>
    private async Task ReleaseAsync(Valour.Database.CdnBucketItem item, DateTime? notBefore)
    {
        item.DriveState = DriveSyncState.Pending;
        item.DriveNextAttemptAt = notBefore;
        await _db.SaveChangesAsync();
    }

    private async Task<SyncOutcome> FailAsync(Valour.Database.CdnBucketItem item, string error, bool permanent)
    {
        item.DriveAttempts++;
        item.DriveError = error;

        if (permanent || item.DriveAttempts >= Config.MaxAttempts)
        {
            item.DriveState = DriveSyncState.Failed;
            item.DriveNextAttemptAt = null;
        }
        else
        {
            // 2, 4, 8 ... minutes, never more than six hours.
            var delay = TimeSpan.FromMinutes(Math.Min(360, Math.Pow(2, item.DriveAttempts)));
            item.DriveState = DriveSyncState.Pending;
            item.DriveNextAttemptAt = DateTime.UtcNow + delay;
        }

        await _db.SaveChangesAsync();
        return SyncOutcome.Failed;
    }

    private async Task<string> UploadToPlacementAsync(
        Valour.Database.CdnBucketItem item,
        string token,
        DrivePlacement placement,
        Stream content,
        CancellationToken cancellationToken)
    {
        var parentId = await EnsureFolderChainAsync(item.UserId, token, placement, cancellationToken);

        var properties = new Dictionary<string, string>
        {
            ["valour"] = "1",
            ["valourSha256"] = item.Sha256Hash ?? string.Empty,
        };
        if (placement.PlanetId is { } planetId)
            properties["valourPlanetId"] = planetId.ToString();

        return await _api.UploadFileAsync(
            token,
            parentId,
            SanitizeName(item.FileName, "file"),
            item.MimeType,
            content,
            item.SizeBytes,
            properties,
            cancellationToken);
    }

    // Folders //

    /// <summary>The folders a file belongs in, from the top of Valour's folder down.</summary>
    public record DrivePlacement(long? PlanetId, IReadOnlyList<(string Key, string Name)> Folders);

    /// <summary>
    /// Decides which folder a file goes in from where it was first used: a
    /// planet, a direct message, or neither.
    /// </summary>
    public async Task<DrivePlacement> ResolvePlacementAsync(Valour.Database.CdnBucketItem item)
    {
        var month = item.CreatedAt.ToString("yyyy-MM");

        var message = await _db.MessageAttachments.AsNoTracking()
            .Where(a => a.CdnBucketItemId == item.Id)
            .OrderBy(a => a.Message.TimeSent)
            .Select(a => new { a.Message.PlanetId, a.Message.ChannelId })
            .FirstOrDefaultAsync();

        if (message?.PlanetId is { } planetId)
        {
            var name = await _db.Planets.AsNoTracking().Where(p => p.Id == planetId).Select(p => p.Name).FirstOrDefaultAsync();
            if (name is not null)
                return PlanetPlacement(planetId, name, month);
        }
        else if (message is not null)
        {
            var name = await GetDirectMessageNameAsync(message.ChannelId, item.UserId);
            return new DrivePlacement(null,
            [
                ("root", "Valour"),
                ("dms", "Direct Messages"),
                ($"dm:{message.ChannelId}", name),
                ($"dm:{message.ChannelId}:{month}", month),
            ]);
        }

        var thread = await _db.ThreadAttachments.AsNoTracking()
            .Where(t => t.CdnBucketItemId == item.Id)
            .Select(t => new { t.Thread.PlanetId })
            .FirstOrDefaultAsync();
        if (thread is not null)
        {
            var name = await _db.Planets.AsNoTracking().Where(p => p.Id == thread.PlanetId).Select(p => p.Name).FirstOrDefaultAsync();
            if (name is not null)
                return PlanetPlacement(thread.PlanetId, name, month);
        }

        return new DrivePlacement(null, [("root", "Valour"), ("other", "Other")]);
    }

    private static DrivePlacement PlanetPlacement(long planetId, string planetName, string month) =>
        new(planetId,
        [
            ("root", "Valour"),
            ("planets", "Planets"),
            ($"planet:{planetId}", planetName),
            ($"planet:{planetId}:{month}", month),
        ]);

    private async Task<string> GetDirectMessageNameAsync(long channelId, long ownerId)
    {
        var others = await _db.ChannelMembers.AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.UserId != ownerId)
            .Select(m => m.User.Name)
            .Take(4)
            .ToListAsync();

        return others.Count switch
        {
            0 => "Direct message",
            <= 3 => string.Join(", ", others),
            _ => string.Join(", ", others.Take(3)) + " and others",
        };
    }

    /// <summary>
    /// Finds or makes each folder from the top down and returns the last one's
    /// ID. Folders are found by saved ID, and renamed to match a planet's new
    /// name when a file next goes in.
    /// </summary>
    private async Task<string> EnsureFolderChainAsync(long userId, string token, DrivePlacement placement, CancellationToken cancellationToken)
    {
        string parentId = null;

        foreach (var (key, rawName) in placement.Folders)
        {
            var name = SanitizeName(rawName, "Untitled");
            var folder = await _db.UserDriveFolders.FirstOrDefaultAsync(x => x.UserId == userId && x.Key == key, cancellationToken);

            if (folder is not null && !await FolderUsableAsync(userId, token, folder, cancellationToken))
            {
                // Gone or trashed in Drive. Everything under it is gone too.
                await ForgetFoldersAsync(userId, key);
                folder = null;
            }

            if (folder is null)
            {
                var id = await _api.CreateFolderAsync(token, name, parentId, cancellationToken);
                folder = new Valour.Database.UserDriveFolder { UserId = userId, Key = key, DriveFolderId = id, Name = name };
                _db.UserDriveFolders.Add(folder);
                await _db.SaveChangesAsync(cancellationToken);
                _cache.Set(FolderCheckKey(userId, key), true, FolderCheckLifetime);
            }
            else if (folder.Name != name && !IsMonth(key))
            {
                // A renamed planet or conversation. People can rename the folder in
                // Drive themselves, so Valour only renames when its own name changed.
                await _api.RenameAsync(token, folder.DriveFolderId, name, cancellationToken);
                folder.Name = name;
                await _db.SaveChangesAsync(cancellationToken);
            }

            parentId = folder.DriveFolderId;
        }

        return parentId;
    }

    private static bool IsMonth(string key) => key.Count(c => c == ':') >= 2;

    private static string FolderCheckKey(long userId, string key) => $"drive-folder:{userId}:{key}";

    private async Task<bool> FolderUsableAsync(long userId, string token, Valour.Database.UserDriveFolder folder, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(FolderCheckKey(userId, folder.Key), out _))
            return true;

        if (!await _api.ExistsAsync(token, folder.DriveFolderId, cancellationToken))
            return false;

        _cache.Set(FolderCheckKey(userId, folder.Key), true, FolderCheckLifetime);
        return true;
    }

    /// <summary>
    /// The key of the folder a folder sits in. The tree is Valour, then Planets,
    /// Direct Messages and Other, then a folder for each planet or conversation,
    /// then a folder for each month.
    /// </summary>
    internal static string ParentKey(string key)
    {
        if (key == "root")
            return null;

        if (key is "planets" or "dms" or "other")
            return "root";

        // planet:1 and dm:5 sit in their group. planet:1:2026-11 sits in planet:1.
        var separator = key.LastIndexOf(':');
        var firstSeparator = key.IndexOf(':');
        if (separator != firstSeparator)
            return key[..separator];

        return key.StartsWith("planet:", StringComparison.Ordinal) ? "planets" : "dms";
    }

    /// <summary>Forgets a folder and the ones inside it, so they are made again.</summary>
    private async Task ForgetFoldersAsync(long userId, string key)
    {
        var all = await _db.UserDriveFolders.Where(x => x.UserId == userId).ToListAsync();

        bool IsInside(string candidate)
        {
            for (var current = candidate; current is not null; current = ParentKey(current))
            {
                if (current == key)
                    return true;
            }

            return false;
        }

        var stale = all.Where(x => IsInside(x.Key)).ToList();

        foreach (var folder in stale)
            _cache.Remove(FolderCheckKey(userId, folder.Key));

        _db.UserDriveFolders.RemoveRange(stale);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Checks each folder from the top down and forgets the first one that is gone,
    /// along with everything inside it. Folders that still exist are kept.
    /// </summary>
    private async Task ForgetMissingFoldersAsync(long userId, string token, DrivePlacement placement, CancellationToken cancellationToken)
    {
        foreach (var (key, _) in placement.Folders)
        {
            var folder = await _db.UserDriveFolders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Key == key, cancellationToken);
            if (folder is null)
                return;

            if (!await _api.ExistsAsync(token, folder.DriveFolderId, cancellationToken))
            {
                await ForgetFoldersAsync(userId, key);
                return;
            }
        }
    }

    /// <summary>A name Drive accepts: no control characters, trimmed, and a sensible length.</summary>
    public static string SanitizeName(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MaxFolderNameLength)
            cleaned = cleaned[..MaxFolderNameLength].TrimEnd();

        return cleaned.Length == 0 ? fallback : cleaned;
    }

    // Removing Valour's copy //

    /// <summary>
    /// Removes Valour's copy of files that have been in Drive long enough. The
    /// stored object is only deleted when no other record still needs it, as
    /// identical files uploaded by different people share one.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Only for connections that work. If Google has cut off access, the Drive
        // copy can't be read, so Valour's copy is kept.
        var due = await (
            from c in _db.UserDriveConnections.AsNoTracking()
            where c.Status == DriveConnectionStatus.Active
            from i in _db.CdnBucketItems
            where i.UserId == c.UserId &&
                  i.DriveState == DriveSyncState.Synced &&
                  i.DriveFileId != null &&
                  i.StoragePurgedAt == null &&
                  i.StorageExpiresAt != null &&
                  i.StorageExpiresAt <= now
            orderby i.StorageExpiresAt
            select new { i.Id, i.Hash })
            .Take(200)
            .ToListAsync(cancellationToken);

        var purged = 0;
        foreach (var entry in due)
        {
            var marked = await _db.CdnBucketItems
                .Where(x => x.Id == entry.Id && x.StoragePurgedAt == null)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.StoragePurgedAt, now), cancellationToken);
            if (marked == 0)
                continue;

            purged++;

            var stillNeeded = await _db.CdnBucketItems.AsNoTracking()
                .AnyAsync(x => x.Hash == entry.Hash && x.StoragePurgedAt == null, cancellationToken);
            if (!stillNeeded)
            {
                var deleted = await _storage.Private.DeleteAsync(entry.Hash);
                if (!deleted.Success)
                    _logger.LogWarning("Could not remove the expired object {Hash}: {Message}", entry.Hash, deleted.Message);
            }
        }

        return purged;
    }

    // Moving one file by hand //

    private Task<Valour.Database.CdnBucketItem> FindOwnedItemAsync(long userId, ContentCategory category, string hash) =>
        _db.CdnBucketItems.FirstOrDefaultAsync(x => x.Id == $"{category}/{userId}/{hash}");

    /// <summary>
    /// Sends a file to Drive and removes Valour's copy. A file on Valour is queued
    /// and its Valour copy goes as soon as the Drive copy exists. A file that is in
    /// Drive with a cached copy on Valour loses that copy at the next pass.
    /// </summary>
    public async Task<TaskResult<UserAttachmentInfo>> SendToDriveAsync(long userId, ContentCategory category, string hash)
    {
        var item = await FindOwnedItemAsync(userId, category, hash);
        if (item is null)
            return TaskResult<UserAttachmentInfo>.FromFailure("Attachment not found.", 404);

        if (item.SafetyQuarantinedAt is not null)
            return TaskResult<UserAttachmentInfo>.FromFailure("This attachment cannot be moved.");

        if (!await UserDriveService.HasActiveConnectionAsync(_db, userId))
            return TaskResult<UserAttachmentInfo>.FromFailure("Connect Google Drive first.");

        switch (item.DriveState)
        {
            case DriveSyncState.Pending or DriveSyncState.Syncing:
                // Already on its way. It no longer waits out the retention days.
                item.DriveReleaseOnSync = true;
                break;

            case DriveSyncState.Synced when item.StoragePurgedAt is not null:
                break;

            case DriveSyncState.Synced:
                item.StorageExpiresAt = DateTime.UtcNow;
                break;

            default:
                // On Valour only, a failed copy, or a file that was moved back to Valour.
                if (item.DriveFileId is not null)
                {
                    // A file moved back earlier may still be in Drive. Reuse that copy
                    // when it is still there instead of uploading it again.
                    try
                    {
                        var token = await _drives.GetAccessTokenAsync(userId);
                        if (await _api.ExistsAsync(token, item.DriveFileId))
                        {
                            item.DriveState = DriveSyncState.Synced;
                            item.DriveSyncedAt = DateTime.UtcNow;
                            item.DriveAttempts = 0;
                            item.DriveError = null;
                            item.StorageExpiresAt = DateTime.UtcNow;
                            break;
                        }
                    }
                    catch (DriveApiException e)
                    {
                        return TaskResult<UserAttachmentInfo>.FromFailure(e.Kind == DriveErrorKind.AccessRevoked
                            ? "Reconnect Google Drive first."
                            : "Valour could not reach Google Drive. Try again in a moment.");
                    }

                    item.DriveFileId = null;
                }

                item.DriveState = DriveSyncState.Pending;
                item.DriveAttempts = 0;
                item.DriveNextAttemptAt = null;
                item.DriveError = null;
                item.DriveReleaseOnSync = true;
                break;
        }

        await _db.SaveChangesAsync();
        return TaskResult<UserAttachmentInfo>.FromData(item.ToInfo());
    }

    /// <summary>
    /// Makes Valour the home of a file that lives in Drive. A file that was only in
    /// Drive is read back first, and needs room in the account's allowance. The
    /// copy in Drive is left alone.
    /// </summary>
    public async Task<TaskResult<UserAttachmentInfo>> MoveToValourAsync(long userId, ContentCategory category, string hash)
    {
        var item = await FindOwnedItemAsync(userId, category, hash);
        if (item is null)
            return TaskResult<UserAttachmentInfo>.FromFailure("Attachment not found.", 404);

        if (item.SafetyQuarantinedAt is not null)
            return TaskResult<UserAttachmentInfo>.FromFailure("This attachment cannot be moved.");

        if (item.DriveState is DriveSyncState.Pending or DriveSyncState.Syncing)
            return TaskResult<UserAttachmentInfo>.FromFailure("This file is still being sent to Google Drive.");

        if (item.DriveState != DriveSyncState.Synced || item.DriveFileId is null)
            return TaskResult<UserAttachmentInfo>.FromFailure("This file is already on Valour.");

        if (item.StoragePurgedAt is not null)
        {
            // Files on Valour count toward the plan's allowance, whatever new uploads do.
            var plan = UserSubscriptionTypes.GetStorageQuotaBytes(await UserStorageService.GetSubscriptionTypeAsync(_db, userId));
            var used = await UserStorageService.GetUsedBytesAsync(_db, userId);
            if (used + item.SizeBytes > plan)
                return TaskResult<UserAttachmentInfo>.FromFailure("There is not enough room on Valour for this file. Free up space or upgrade first.", 413);

            if (!await RestoreAsync(item))
                return TaskResult<UserAttachmentInfo>.FromFailure("Valour could not read this file from Google Drive. Check that Drive is connected and try again.");
        }

        item.DriveState = DriveSyncState.Pinned;
        item.StorageExpiresAt = null;
        item.DriveReleaseOnSync = false;
        await _db.SaveChangesAsync();
        return TaskResult<UserAttachmentInfo>.FromData(item.ToInfo());
    }

    // Reading a file back from Drive //

    /// <summary>
    /// Puts a purged file's bytes back on Valour's storage from the owner's
    /// Drive, and starts a new expiry. Returns false when the file can't be read
    /// back, such as when Drive was disconnected or the file was deleted there.
    /// </summary>
    public Task<bool> RestoreAsync(Valour.Database.CdnBucketItem item)
    {
        if (item.StoragePurgedAt is null)
            return Task.FromResult(true);

        var itemId = item.Id;
        var fill = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = RestoresInFlight.GetOrAdd(itemId, fill.Task);
        if (inFlight != fill.Task)
            return inFlight;

        return RestoreAndCompleteAsync(item, fill);
    }

    private async Task<bool> RestoreAndCompleteAsync(Valour.Database.CdnBucketItem item, TaskCompletionSource<bool> fill)
    {
        var ok = false;
        await RestoreGate.WaitAsync();
        try
        {
            ok = await RestoreCoreAsync(item);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not read {ItemId} back from Google Drive", item.Id);
        }
        finally
        {
            RestoreGate.Release();
            fill.SetResult(ok);
            RestoresInFlight.TryRemove(new KeyValuePair<string, Task<bool>>(item.Id, fill.Task));
        }

        return ok;
    }

    private async Task<bool> RestoreCoreAsync(Valour.Database.CdnBucketItem item)
    {
        if (item.DriveFileId is null)
            return false;

        var token = await _drives.GetAccessTokenAsync(item.UserId);

        await using var download = await _api.OpenDownloadAsync(token, item.DriveFileId);

        var buffer = new MemoryStream(Math.Max(0, item.SizeBytes));
        await download.Stream.CopyToAsync(buffer);

        // Whatever comes back from Drive could have been changed there, so it has
        // to be the same file before Valour serves it under this address.
        if (item.Sha256Hash is not null)
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
            if (!string.Equals(actual, item.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("The Drive copy of {ItemId} no longer matches the file that was uploaded", item.Id);
                return false;
            }
        }

        buffer.Position = 0;
        var put = await _storage.Private.PutAsync(item.Hash, buffer, item.MimeType);
        if (!put.Success)
            return false;

        var expires = DateTime.UtcNow.AddDays(Config.RetentionDays);
        await _db.CdnBucketItems
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(i => i.StoragePurgedAt, (DateTime?)null)
                .SetProperty(i => i.StorageExpiresAt, expires));

        item.StoragePurgedAt = null;
        item.StorageExpiresAt = expires;
        return true;
    }
}
