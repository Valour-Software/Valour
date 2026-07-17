using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using CloudFlare.Client;
using CloudFlare.Client.Api.Zones;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Cdn.Storage;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Server.Cdn;

public class CdnBucketService
{
    private static readonly ConcurrentDictionary<string, Task<TaskResult>> PublicUploadsInFlight = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<TaskResult>> PrivateUploadsInFlight = new(StringComparer.Ordinal);

    private readonly ILogger<CdnBucketService> _logger;
    private readonly ICloudFlareClient _cloudflare;
    private readonly CdnStorageProvider _storage;
    private readonly string _zone;

    public CdnBucketService(ICloudFlareClient cloudflare, CdnStorageProvider storage, ILogger<CdnBucketService> logger)
    {
        _cloudflare = cloudflare;
        _storage = storage;
        _logger = logger;
        _zone = CloudflareConfig.Instance?.ZoneId;
    }

    public async Task<TaskResult> UploadPublicImage(Stream data, string path)
    {
        while (true)
        {
            if (PublicUploadsInFlight.TryGetValue(path, out var existingTask))
            {
                return await existingTask;
            }

            var taskSource = new TaskCompletionSource<TaskResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!PublicUploadsInFlight.TryAdd(path, taskSource.Task))
            {
                continue;
            }

            TaskResult result;
            try
            {
                result = await UploadPublicImageCoreAsync(data, path);
                taskSource.SetResult(result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error while uploading public image to {Path}", path);
                result = new TaskResult(false, "Failed to upload public image.");
                taskSource.SetResult(result);
            }
            finally
            {
                PublicUploadsInFlight.TryRemove(new KeyValuePair<string, Task<TaskResult>>(path, taskSource.Task));
            }

            return result;
        }
    }

    private async Task<TaskResult> UploadPublicImageCoreAsync(Stream data, string path)
    {
        // Get MIME type from file extension
        var extension = Path.GetExtension(path);
        CdnUtils.ExtensionToMimeType.TryGetValue(extension, out var mimeType);
        mimeType ??= "application/octet-stream";

        var putResult = await _storage.Public.PutAsync(path, data, mimeType);
        if (!putResult.Success)
        {
            _logger.LogError("Failed to PUT public object: {Path} ({Message})", path, putResult.Message);
            return putResult;
        }

        if (!string.IsNullOrWhiteSpace(_zone))
        {
            // Purge the cache for this object. Public URLs live under the
            // fixed /valour-public/ namespace on the public CDN host.
            var publicUrl = $"{ValourHosts.PublicCdnBaseUrl}/valour-public/{path}";
            var purgeResult = await _cloudflare.Zones.PurgeFilesAsync(_zone, [publicUrl]);

            if (!purgeResult.Success)
            {
                _logger.LogError("Failed to purge cache for {Path}: {Error}", path, purgeResult.Errors);
            }
            else
            {
                _logger.LogInformation("Successfully purged cache for {Path}", path);
            }
        }

        _logger.LogInformation("Successfully PUT public object: {Path}", path);

        return new TaskResult(true, $"{ValourHosts.PublicCdnBaseUrl}/valour-public/{path}");
    }

    public async Task<TaskResult> Upload(
        MemoryStream data,
        string fileName,
        string extension,
        long userId,
        string mime,
        ContentCategory category,
        ValourDb db,
        MediaSafetyHashMatchResult safetyHashMatch = null)
    {
        // Get hash from image
        var hashBytes = SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length));
        var sha256Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        var hash = sha256Hash;

        // Add file extension to the end
        hash += extension;

        // Check if this same file has already been posted by this user.
        var id = $"{category}/{userId}/{hash}";

        // If so, return the location (wooo easy route)
        var existingUserItem = await db.CdnBucketItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (existingUserItem is not null)
        {
            if (existingUserItem.SafetyQuarantinedAt is not null)
            {
                return new TaskResult(false, "This upload is not available.");
            }

            return new TaskResult(true, $"{ValourHosts.ContentCdnBaseUrl}/content/{id}");
        }

        // We need a bucket record no matter what at this point
        var bucketRecord = new CdnBucketItem()
        {
            Id = id,
            Category = category,
            Hash = hash,
            MimeType = mime,
            UserId = userId,
            FileName = fileName,
            SizeBytes = (int)data.Length,
            CreatedAt = DateTime.UtcNow,
            Sha256Hash = sha256Hash,
            SafetyHashMatchState = MediaSafetyHashMatchState.Skipped
        };

        ApplySafetyHashMatch(bucketRecord, safetyHashMatch);

        // Now we check if anyone else has already posted this file.
        // If so, we can just create a new path to the file
        var existingHashItem = await db.CdnBucketItems.AsNoTracking().FirstOrDefaultAsync(x => x.Hash == hash);
        if (existingHashItem is not null)
        {
            if (existingHashItem.SafetyQuarantinedAt is not null)
            {
                return new TaskResult(false, "This upload is not available.");
            }

            // Alright, someone else posted this. Let's make a new route to this
            // object without actually re-uploading it.
            try
            {
                await db.CdnBucketItems.AddAsync(bucketRecord);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race condition: another request inserted this row between our AnyAsync check and SaveChangesAsync.
                // Detach the failed entity so the DbContext isn't corrupted for any future operations.
                var entry = db.Entry(bucketRecord);
                if (entry.State == EntityState.Added)
                    entry.State = EntityState.Detached;

                if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
                    return new TaskResult(true, $"{ValourHosts.ContentCdnBaseUrl}/content/{id}");

                _logger.LogError("DbUpdateException when adding new route to existing bucket item, and row still not found for {Id}", id);
                return new TaskResult(false, "Critical error when adding new route to existing bucket item.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Critical error when adding new route to existing bucket item");
                return new TaskResult(false, "Critical error when adding new route to existing bucket item.");
            }

            return new TaskResult(true, $"{ValourHosts.ContentCdnBaseUrl}/content/{id}");
        }

        var uploadResult = await UploadPrivateObjectDedupedAsync(data, hash, mime);
        if (!uploadResult.Success)
        {
            return uploadResult;
        }

        try
        {
            await db.CdnBucketItems.AddAsync(bucketRecord);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race condition: another request inserted this row between our AnyAsync check and SaveChangesAsync.
            // Detach the failed entity so the DbContext isn't corrupted for any future operations.
            var entry = db.Entry(bucketRecord);
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;

            if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
                return new TaskResult(true, $"{ValourHosts.ContentCdnBaseUrl}/content/{id}");

            _logger.LogError("DbUpdateException when adding route to new bucket item, and row still not found for {Id}", id);
            return new TaskResult(false, "Critical error when adding route to new bucket item.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Critical error when adding route to new bucket item");
            return new TaskResult(false, "Critical error when adding route to new bucket item.");
        }

        return new TaskResult(true, $"{ValourHosts.ContentCdnBaseUrl}/content/{id}");
    }

    public async Task<TaskResult> DeletePrivateObjectIfUnusedAsync(string hash, ValourDb db)
    {
        if (await db.CdnBucketItems.AsNoTracking().AnyAsync(x => x.Hash == hash))
            return TaskResult.SuccessResult;

        return await _storage.Private.DeleteAsync(hash);
    }

    private static void ApplySafetyHashMatch(CdnBucketItem item, MediaSafetyHashMatchResult safetyHashMatch)
    {
        if (safetyHashMatch is null)
            return;

        item.SafetyHashMatchState = safetyHashMatch.State;
        item.SafetyProvider = safetyHashMatch.Provider;
        item.SafetyHashMatchedAt = safetyHashMatch.HashMatchedAt;
        item.SafetyMatchId = safetyHashMatch.MatchId;
        item.SafetyDetails = safetyHashMatch.Details;

        if (safetyHashMatch.ShouldBlock)
            item.SafetyQuarantinedAt = DateTime.UtcNow;
    }

    private async Task<TaskResult> UploadPrivateObjectDedupedAsync(MemoryStream data, string hash, string mime)
    {
        while (true)
        {
            if (PrivateUploadsInFlight.TryGetValue(hash, out var existingTask))
            {
                return await existingTask;
            }

            var taskSource = new TaskCompletionSource<TaskResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!PrivateUploadsInFlight.TryAdd(hash, taskSource.Task))
            {
                continue;
            }

            TaskResult result;
            try
            {
                result = await UploadPrivateObjectCoreAsync(data, hash, mime);
                taskSource.SetResult(result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error while uploading object to bucket: {Hash}", hash);
                result = new TaskResult(false, "Failed to upload object to bucket.");
                taskSource.SetResult(result);
            }
            finally
            {
                PrivateUploadsInFlight.TryRemove(new KeyValuePair<string, Task<TaskResult>>(hash, taskSource.Task));
            }

            return result;
        }
    }

    private async Task<TaskResult> UploadPrivateObjectCoreAsync(MemoryStream data, string hash, string mime)
    {
        // This object is unique and has to be posted to storage
        return await _storage.Private.PutAsync(hash, data, mime);
    }
}
