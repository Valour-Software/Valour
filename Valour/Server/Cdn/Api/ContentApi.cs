using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Valour.Server.Cdn.Storage;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Api;

public class ContentApi : Controller
{
    /// <summary>
    /// Objects up to this size are kept in memory after the first request.
    /// Larger objects are streamed from storage on every request so a single
    /// anonymous download never buffers the whole file.
    /// </summary>
    internal const int MaxCachedObjectBytes = 4 * 1024 * 1024;

    /// <summary>
    /// One storage read per object while its cache entry is being filled, so a
    /// burst of requests for the same uncached object does not multiply reads.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<byte[]>> CacheFillsInFlight = new(StringComparer.Ordinal);

    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/content/{category}/{userId}/{hash}", GetRoute);
        app.MapGet("/content/{category}/{userId}/{hash}/signed", GetSignedUrlRoute);
    }

    private static async Task<IResult> GetSignedUrlRoute(CdnMemoryCache cache, ValourDb db, CdnStorageProvider storage, ContentCategory category, string hash, ulong userId)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Results.BadRequest("Include id.");

        if (userId == 0)
            return Results.BadRequest("Include user id.");

        var id = $"{category}/{userId}/{hash}";

        var bucketItem = await db.CdnBucketItems.FindAsync(id);
        if (bucketItem is null)
            return Results.NotFound();

        if (IsUnavailable(bucketItem))
            return Results.NotFound();

        var url = await GetSignedUrlAsync(cache, storage, bucketItem);
        if (string.IsNullOrWhiteSpace(url))
            return Results.BadRequest("Failed to generate pre-signed URL.");

        return ValourResult.Ok(url);
    }

    public static async Task<string> GetSignedUrlAsync(CdnMemoryCache cache, CdnStorageProvider storage, Valour.Database.CdnBucketItem bucketItem)
    {
        if (bucketItem is null || IsUnavailable(bucketItem))
            return null;

        // Backends without signing (filesystem mode) fall back to the direct
        // content route, which streams through the server with auth-free access
        // identical to what a signed URL would grant.
        if (!storage.Private.SupportsSignedUrls)
            return $"{ValourHosts.ContentCdnBaseUrl}/content/{bucketItem.Id}";

        var cacheKey = $"signed:{bucketItem.Id}:{bucketItem.MimeType}:{bucketItem.FileName}";

        if (cache.Cache.TryGetValue(cacheKey, out string cachedUrl))
            return cachedUrl;

        var url = await storage.Private.GetSignedUrlAsync(
            bucketItem.Hash, bucketItem.MimeType, bucketItem.FileName, TimeSpan.FromHours(1));

        if (string.IsNullOrWhiteSpace(url))
            return null;

        cache.Cache.Set(cacheKey, url,
            new MemoryCacheEntryOptions()
            {
                Size = (cacheKey.Length + url.Length) * sizeof(char),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            }
        );

        return url;
    }

    private static async Task<IResult> GetRoute(HttpContext ctx, CdnMemoryCache cache, ValourDb db, CdnStorageProvider storage,
         ContentCategory category, string hash, ulong userId)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Results.BadRequest("Include id.");

        if (userId == 0)
            return Results.BadRequest("Include user id.");

        var id = $"{category}/{userId}/{hash}";

        var bucketItemRecord = await db.CdnBucketItems.FindAsync(id);
        if (bucketItemRecord is null)
            return Results.NotFound();

        if (IsUnavailable(bucketItemRecord))
            return Results.NotFound();

        // Stored names and types are uploader-controlled; only non-scriptable
        // media is ever served inline on a Valour origin.
        var headers = CdnServePolicy.Resolve(bucketItemRecord.FileName, bucketItemRecord.MimeType);
        CdnServePolicy.Apply(ctx.Response, headers);

        if (bucketItemRecord.SizeBytes <= MaxCachedObjectBytes)
        {
            var data = await GetCachedObjectAsync(cache, storage, hash, bucketItemRecord.SizeBytes);
            if (data is not null)
                return Results.File(data, headers.ContentType, enableRangeProcessing: true);
        }

        var download = await storage.Private.GetAsync(hash);
        if (download is null)
            return Results.NotFound();

        // Results.Stream disposes the stream; the download also owns the
        // backend response the stream came from.
        ctx.Response.RegisterForDisposeAsync(download);
        return Results.Stream(download.Stream, headers.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Returns a small object from the memory cache, filling it from storage at
    /// most once at a time. Returns null when the object is missing or larger
    /// than its recorded size, so the caller streams it instead.
    /// </summary>
    private static async Task<byte[]> GetCachedObjectAsync(CdnMemoryCache cache, CdnStorageProvider storage, string hash, int expectedBytes)
    {
        var cacheKey = $"content:{hash}";

        if (cache.Cache.TryGetValue(cacheKey, out byte[] cached))
            return cached;

        var fill = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = CacheFillsInFlight.GetOrAdd(cacheKey, fill.Task);
        if (inFlight != fill.Task)
            return await inFlight;

        byte[] data = null;
        try
        {
            data = await ReadSmallObjectAsync(storage, hash, expectedBytes);
            if (data is not null)
            {
                cache.Cache.Set(cacheKey, data,
                    new MemoryCacheEntryOptions()
                    {
                        Size = data.Length,
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                    });
            }
        }
        finally
        {
            fill.SetResult(data);
            CacheFillsInFlight.TryRemove(new KeyValuePair<string, Task<byte[]>>(cacheKey, fill.Task));
        }

        return data;
    }

    private static async Task<byte[]> ReadSmallObjectAsync(CdnStorageProvider storage, string hash, int expectedBytes)
    {
        var download = await storage.Private.GetAsync(hash);
        if (download is null)
            return null;

        await using (download)
        {
            // One byte past the recorded size is enough to detect an object
            // that is larger than its record, which is then streamed instead.
            var limit = Math.Clamp(expectedBytes, 0, MaxCachedObjectBytes);
            var buffer = new byte[limit + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await download.Stream.ReadAsync(buffer.AsMemory(total));
                if (read == 0)
                    break;

                total += read;
            }

            return total > limit ? null : buffer[..total];
        }
    }

    private static bool IsUnavailable(Valour.Database.CdnBucketItem item)
    {
        return item.SafetyQuarantinedAt is not null;
    }
}
