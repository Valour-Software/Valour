using Amazon.S3;
using Amazon.S3.Model;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using CloudFlare.Client;
using CloudFlare.Client.Api.Zones;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Shared;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn;

public class CdnBucketService
{
    public static AmazonS3Client Client { get; set; }
    public static AmazonS3Client PublicClient { get; set; }
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(1500)
    };
    private static readonly ConcurrentDictionary<string, Task<TaskResult>> PublicUploadsInFlight = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<TaskResult>> PrivateUploadsInFlight = new(StringComparer.Ordinal);

    private readonly ILogger<CdnBucketService> _logger;
    private readonly ICloudFlareClient _cloudflare;
    private readonly string _zone;
    
    public CdnBucketService(ICloudFlareClient cloudflare, ILogger<CdnBucketService> logger)
    {
        _cloudflare = cloudflare;
        _logger = logger;
        _zone = CloudflareConfig.Instance.ZoneId;
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

        PutObjectRequest request = new()
        {
            Key = path,
            InputStream = data,
            BucketName = "valour-public",
            DisablePayloadSigning = true,
            ContentType = mimeType
        };
        
        try
        {
            var response = await PutObjectWithRetryAsync(PublicClient, request, path);
            
            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                if (!string.IsNullOrWhiteSpace(_zone))
                {
                    // Purge the cache for this object
                    var purgeResult =
                        await _cloudflare.Zones.PurgeFilesAsync(_zone, [$"https://public-cdn.valour.gg/{path}"]);

                    if (!purgeResult.Success)
                    {
                        _logger.LogError("Failed to purge cache for {Path}: {Error}", path, purgeResult.Errors);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully purged cache for {Path}", path);
                    }
                }
                
                _logger.LogInformation("Successfully PUT object into bucket: {Path}", path);

                return new TaskResult(true, $"https://public-cdn.valour.gg/{path}");
            }
            else
            {
                _logger.LogError("Failed to PUT object into bucket: {Path} ({StatusCode})", path, response.HttpStatusCode);
                return new TaskResult(false, $"Failed to PUT object into bucket. ({response.HttpStatusCode})");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to upload public image to {Path}", path);
            return new TaskResult(false, "Failed to upload public image.");
        }
    }

    public async Task<TaskResult> Upload(MemoryStream data, string fileName, string extension, long userId, string mime, 
        ContentCategory category, ValourDb db)
    {
        // Get hash from image
        var hashBytes = SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

        // Add file extension to the end
        hash += extension;

        // Check if this same file has already been posted by this user.
        var id = $"{category}/{userId}/{hash}";

        // If so, return the location (wooo easy route)
        if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
            return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");

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
            CreatedAt = DateTime.UtcNow
        };

        // Now we check if anyone else has already posted this file.
        // If so, we can just create a new path to the file
        if (await db.CdnBucketItems.AnyAsync(x => x.Hash == hash))
        {
            // Alright, someone else posted this. Let's make a new route to this
            // object without actually re-uploading it.
            try
            {
                await db.CdnBucketItems.AddAsync(bucketRecord);
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
                    return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");

                _logger.LogError(e, "Critical error when adding new route to existing bucket item");
                return new TaskResult(false, "Critical error when adding new route to existing bucket item.");
            }

            return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");
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
        catch (Exception e)
        {
            if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
                return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");

            _logger.LogError(e, "Critical error when adding route to new bucket item");
            return new TaskResult(false, "Critical error when adding route to new bucket item.");
        }

        return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");
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
        // This object is unique and has to be posted to the bucket
        PutObjectRequest request = new()
        {
            Key = hash,
            // ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            InputStream = data,
            BucketName = "valourmps",
            DisablePayloadSigning = true,
            ContentType = mime
        };

        PutObjectResponse response;
        try
        {
            response = await PutObjectWithRetryAsync(Client, request, hash);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to upload object to bucket: {Hash}", hash);
            return new TaskResult(false, "Failed to upload object to bucket.");
        }

        if (!CdnUtils.IsSuccessStatusCode(response.HttpStatusCode))
        {
            return new TaskResult(false, $"Failed to PUT object into bucket. ({response.HttpStatusCode})");
        }

        return TaskResult.SuccessResult;
    }

    private async Task<PutObjectResponse> PutObjectWithRetryAsync(
        AmazonS3Client client,
        PutObjectRequest request,
        string key)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (request.InputStream.CanSeek)
                {
                    request.InputStream.Position = 0;
                }

                return await client.PutObjectAsync(request);
            }
            catch (Exception ex) when (ShouldRetryPut(ex) && attempt < RetryDelays.Length)
            {
                var delay = RetryDelays[attempt];
                _logger.LogWarning(
                    ex,
                    "Transient S3 upload error for {Key}. Retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts})",
                    key,
                    (int)delay.TotalMilliseconds,
                    attempt + 1,
                    RetryDelays.Length + 1);

                await Task.Delay(delay);
            }
        }
    }

    private static bool ShouldRetryPut(Exception ex)
    {
        if (ex is AmazonS3Exception s3Ex)
        {
            if (s3Ex.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError)
                return true;

            if (string.Equals(s3Ex.ErrorCode, "SlowDown", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("Reduce your concurrent request rate for the same object", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Slow Down", StringComparison.OrdinalIgnoreCase);
    }
}
