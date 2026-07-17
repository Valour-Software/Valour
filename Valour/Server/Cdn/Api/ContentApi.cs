using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Valour.Server.Cdn.Storage;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Api;

public class ContentApi : Controller
{
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

    private static async Task<IResult> GetRoute(CdnMemoryCache cache, ValourDb db, CdnStorageProvider storage,
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

        byte[] data;

        if (!cache.Cache.TryGetValue(hash, out data))
        {
            var download = await storage.Private.GetAsync(hash);
            if (download is null)
                return Results.NotFound();

            await using (download)
            {
                MemoryStream ms = new MemoryStream();
                await download.Stream.CopyToAsync(ms);
                data = ms.ToArray();
            }

            cache.Cache.Set(hash, data,
                new MemoryCacheEntryOptions()
                {
                    Size = data.Length,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                }
             );
        }

        return Results.File(data, bucketItemRecord.MimeType, bucketItemRecord.FileName, enableRangeProcessing: true);
    }

    private static bool IsUnavailable(Valour.Database.CdnBucketItem item)
    {
        return item.SafetyQuarantinedAt is not null;
    }
}
