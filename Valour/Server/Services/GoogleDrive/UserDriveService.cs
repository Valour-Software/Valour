using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services.GoogleDrive;

/// <summary>
/// Which accounts have connected a Google Drive, and the access Valour uses to
/// reach it. The refresh token is encrypted at rest. Valour keeps access tokens
/// in memory only until they expire.
/// </summary>
public class UserDriveService
{
    private const string ProtectorPurpose = "Valour.GoogleDrive.RefreshToken";

    private readonly ValourDb _db;
    private readonly IGoogleDriveApi _api;
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserDriveService> _logger;

    public UserDriveService(
        ValourDb db,
        IGoogleDriveApi api,
        IDataProtectionProvider dataProtection,
        IMemoryCache cache,
        ILogger<UserDriveService> logger)
    {
        _db = db;
        _api = api;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// True when this server can connect Google Drive: the feature is on and
    /// Google has an OAuth client.
    /// </summary>
    public static bool IsAvailable =>
        GoogleDriveConfig.Current.Enabled && ExternalAuthConfig.Current?.Google?.IsConfigured == true;

    public static Task<bool> HasActiveConnectionAsync(ValourDb db, long userId) =>
        db.UserDriveConnections.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.Status == DriveConnectionStatus.Active);

    /// <summary>
    /// True when the account has a working Drive and new uploads go to it. Only
    /// then is the storage allowance lifted and the file limit raised.
    /// </summary>
    public static Task<bool> IsDriveDefaultAsync(ValourDb db, long userId) =>
        db.UserDriveConnections.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.Status == DriveConnectionStatus.Active && x.AutoSyncFrom != null);

    public Task<Valour.Database.UserDriveConnection> GetConnectionAsync(long userId) =>
        _db.UserDriveConnections.FirstOrDefaultAsync(x => x.UserId == userId);

    // Connecting //

    /// <summary>
    /// Stores a Drive the person just allowed. Connecting the same Google account
    /// again keeps the files already in it. A different account starts fresh.
    /// </summary>
    public async Task<TaskResult> ConnectAsync(long userId, string googleAccountId, string email, string refreshToken)
    {
        if (!IsAvailable)
            return TaskResult.FromFailure("Google Drive is not available on this server.");

        if (string.IsNullOrWhiteSpace(googleAccountId) || string.IsNullOrWhiteSpace(refreshToken))
            return TaskResult.FromFailure("Google did not return Drive access.");

        var connection = await GetConnectionAsync(userId);
        var sameAccount = connection is not null && connection.GoogleAccountId == googleAccountId;

        // Check the access works before telling the person it does.
        try
        {
            await _api.RefreshAccessTokenAsync(refreshToken);
        }
        catch (DriveApiException e)
        {
            _logger.LogWarning(e, "A new Drive connection could not be verified");
            return TaskResult.FromFailure("Valour could not use that Google Drive. Try connecting again.");
        }

        if (connection is null)
        {
            connection = new Valour.Database.UserDriveConnection
            {
                UserId = userId,
                ConnectedAt = DateTime.UtcNow,
                // New uploads go to Drive until the person says otherwise.
                AutoSyncFrom = DateTime.UtcNow,
            };
            _db.UserDriveConnections.Add(connection);
        }
        else if (!sameAccount)
        {
            // Another Drive has none of the files or folders Valour made in the old one.
            await ForgetDriveContentsAsync(userId);
            connection.ConnectedAt = DateTime.UtcNow;
            connection.AutoSyncFrom = DateTime.UtcNow;
        }
        else if (connection.Status == DriveConnectionStatus.NeedsReconnect)
        {
            // Files that stopped copying while access was broken try again.
            await _db.CdnBucketItems
                .Where(x => x.UserId == userId && x.DriveState == DriveSyncState.Failed)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(i => i.DriveState, DriveSyncState.Pending)
                    .SetProperty(i => i.DriveAttempts, 0)
                    .SetProperty(i => i.DriveNextAttemptAt, (DateTime?)null)
                    .SetProperty(i => i.DriveError, (string)null));
        }

        connection.GoogleAccountId = googleAccountId;
        connection.Email = email;
        connection.RefreshTokenEncrypted = _protector.Protect(refreshToken);
        connection.Status = DriveConnectionStatus.Active;
        connection.LastError = null;

        await _db.SaveChangesAsync();
        _cache.Remove(TokenCacheKey(userId));
        return TaskResult.SuccessResult;
    }

    /// <summary>Chooses whether new uploads go to Drive or stay on Valour.</summary>
    public async Task<TaskResult> SetNewUploadsToDriveAsync(long userId, bool toDrive)
    {
        var connection = await GetConnectionAsync(userId);
        if (connection is null)
            return TaskResult.FromFailure("Google Drive is not connected.");

        // Only files uploaded after the switch are affected, so turning it back on
        // does not sweep up the files that were kept on Valour in between.
        var enabled = connection.AutoSyncFrom is not null;
        if (toDrive && !enabled)
            connection.AutoSyncFrom = DateTime.UtcNow;
        else if (!toDrive)
            connection.AutoSyncFrom = null;

        await _db.SaveChangesAsync();
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Removes Valour's access. Files that were only on Drive stay recorded, so
    /// connecting the same account again makes them readable again.
    /// </summary>
    public async Task<TaskResult> DisconnectAsync(long userId)
    {
        var connection = await GetConnectionAsync(userId);
        if (connection is null)
            return TaskResult.SuccessResult;

        string refreshToken = null;
        try
        {
            refreshToken = _protector.Unprotect(connection.RefreshTokenEncrypted);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "A stored Drive token could not be read while disconnecting");
        }

        _db.UserDriveConnections.Remove(connection);

        // Files waiting for Drive stay on Valour. Files already copied but still
        // on Valour become ordinary files again, and no longer expire.
        await ForgetDriveContentsAsync(userId);
        await _db.SaveChangesAsync();
        _cache.Remove(TokenCacheKey(userId));

        if (refreshToken is not null)
            await _api.RevokeAsync(refreshToken);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// The account's saved Drive refresh token in the clear, or null when there
    /// is none or it can't be read. Account deletion reads it before the
    /// connection record is removed, so the grant can be revoked afterwards.
    /// </summary>
    public async Task<string> ReadRefreshTokenAsync(long userId)
    {
        var encrypted = await _db.UserDriveConnections.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.RefreshTokenEncrypted)
            .FirstOrDefaultAsync();

        if (encrypted is null)
            return null;

        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "A stored Drive token could not be read");
            return null;
        }
    }

    /// <summary>Tells Google to end a Drive grant. Failures are logged and ignored.</summary>
    public Task RevokeGrantAsync(string refreshToken) => _api.RevokeAsync(refreshToken);

    /// <summary>
    /// Files copied into another Drive can't be reached with this one. Those
    /// still on Valour are copied again. Those only in the old Drive are lost.
    /// </summary>
    private async Task ForgetDriveContentsAsync(long userId)
    {
        await _db.CdnBucketItems
            .Where(x => x.UserId == userId && x.StoragePurgedAt == null && x.DriveState != DriveSyncState.None)
            .ExecuteUpdateAsync(x => x
                .SetProperty(i => i.DriveState, DriveSyncState.None)
                .SetProperty(i => i.DriveFileId, (string)null)
                .SetProperty(i => i.DriveSyncedAt, (DateTime?)null)
                .SetProperty(i => i.DriveAttempts, 0)
                .SetProperty(i => i.DriveNextAttemptAt, (DateTime?)null)
                .SetProperty(i => i.DriveError, (string)null)
                .SetProperty(i => i.StorageExpiresAt, (DateTime?)null));

        await _db.UserDriveFolders.Where(x => x.UserId == userId).ExecuteDeleteAsync();
    }

    // Reaching the Drive //

    private static string TokenCacheKey(long userId) => $"drive-token:{userId}";

    /// <summary>
    /// A working access token for the account's Drive. Throws
    /// <see cref="DriveApiException"/>: <see cref="DriveErrorKind.AccessRevoked"/>
    /// when there is no connection or Google refuses it, which also marks the
    /// connection as needing to be reconnected.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(TokenCacheKey(userId), out DriveAccessToken cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Token;

        var connection = await GetConnectionAsync(userId);
        if (connection is null || connection.Status != DriveConnectionStatus.Active)
            throw new DriveApiException(DriveErrorKind.AccessRevoked, "Google Drive is not connected.");

        string refreshToken;
        try
        {
            refreshToken = _protector.Unprotect(connection.RefreshTokenEncrypted);
        }
        catch (Exception e)
        {
            // The key ring that protected the token is gone, so it can't be recovered.
            _logger.LogError(e, "A stored Drive token could not be decrypted");
            await MarkNeedsReconnectAsync(connection, "Valour could not read the saved Google access.");
            throw new DriveApiException(DriveErrorKind.AccessRevoked, "The saved Google access can't be read.", e);
        }

        try
        {
            var token = await _api.RefreshAccessTokenAsync(refreshToken, cancellationToken);
            _cache.Set(TokenCacheKey(userId), token, token.ExpiresAt);
            return token.Token;
        }
        catch (DriveApiException e) when (e.Kind == DriveErrorKind.AccessRevoked)
        {
            await MarkNeedsReconnectAsync(connection, "Google no longer allows Valour to use this Drive. Reconnect it.");
            throw;
        }
    }

    private async Task MarkNeedsReconnectAsync(Valour.Database.UserDriveConnection connection, string error)
    {
        connection.Status = DriveConnectionStatus.NeedsReconnect;
        connection.LastError = error;
        await _db.SaveChangesAsync();
        _cache.Remove(TokenCacheKey(connection.UserId));
    }

    /// <summary>Records a problem with the Drive that the person can see, or clears it.</summary>
    public Task SetLastErrorAsync(long userId, string error) =>
        _db.UserDriveConnections.Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.LastError, error));

    // Reporting //

    public async Task<DriveConnectionInfo> GetInfoAsync(long userId)
    {
        var info = new DriveConnectionInfo
        {
            Available = IsAvailable,
            MinUploadBytes = GoogleDriveLimits.ConnectedMaxUploadBytes,
        };

        var connection = await _db.UserDriveConnections.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (connection is null)
            return info;

        info.Connected = true;
        info.NewUploadsToDrive = connection.AutoSyncFrom is not null;
        info.Email = connection.Email;
        info.Status = connection.Status;
        info.LastSyncAt = connection.LastSyncAt;
        info.LastError = connection.LastError;

        var autoSyncFrom = connection.AutoSyncFrom ?? DateTime.MaxValue;
        var rows = await _db.CdnBucketItems.AsNoTracking()
            .Where(x => x.UserId == userId && x.SafetyQuarantinedAt == null)
            .GroupBy(x => new { x.DriveState, Purged = x.StoragePurgedAt != null, IsNew = x.CreatedAt >= autoSyncFrom })
            .Select(g => new { g.Key.DriveState, g.Key.Purged, g.Key.IsNew, Count = g.Count(), Bytes = g.Sum(x => (long)x.SizeBytes) })
            .ToListAsync();

        foreach (var row in rows)
        {
            switch (row.DriveState)
            {
                case DriveSyncState.Synced:
                    info.SyncedCount += row.Count;
                    info.SyncedBytes += row.Bytes;
                    break;
                case DriveSyncState.Pending:
                case DriveSyncState.Syncing:
                    info.PendingCount += row.Count;
                    break;
                case DriveSyncState.Failed:
                    info.FailedCount += row.Count;
                    break;
                case DriveSyncState.Pinned:
                    // The person chose to keep these on Valour.
                    break;
                default:
                    // New uploads are picked up by the worker on its own.
                    if (row.IsNew)
                        info.PendingCount += row.Count;
                    else
                    {
                        info.UnsyncedCount += row.Count;
                        info.UnsyncedBytes += row.Bytes;
                    }
                    break;
            }
        }

        return info;
    }

    // Queueing //

    /// <summary>Queues the files uploaded before Drive was connected.</summary>
    public async Task<int> QueueExistingFilesAsync(long userId)
    {
        if (!await HasActiveConnectionAsync(_db, userId))
            return 0;

        return await _db.CdnBucketItems
            .Where(x => x.UserId == userId &&
                        x.DriveState == DriveSyncState.None &&
                        x.DriveFileId == null &&
                        x.StoragePurgedAt == null &&
                        x.SafetyQuarantinedAt == null)
            .ExecuteUpdateAsync(x => x
                .SetProperty(i => i.DriveState, DriveSyncState.Pending)
                .SetProperty(i => i.DriveAttempts, 0)
                .SetProperty(i => i.DriveNextAttemptAt, (DateTime?)null)
                .SetProperty(i => i.DriveError, (string)null));
    }

    /// <summary>Tries again the files whose copy failed.</summary>
    public async Task<int> RetryFailedAsync(long userId)
    {
        if (!await HasActiveConnectionAsync(_db, userId))
            return 0;

        return await _db.CdnBucketItems
            .Where(x => x.UserId == userId && x.DriveState == DriveSyncState.Failed)
            .ExecuteUpdateAsync(x => x
                .SetProperty(i => i.DriveState, DriveSyncState.Pending)
                .SetProperty(i => i.DriveAttempts, 0)
                .SetProperty(i => i.DriveNextAttemptAt, (DateTime?)null)
                .SetProperty(i => i.DriveError, (string)null));
    }
}
