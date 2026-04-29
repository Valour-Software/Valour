using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Api;

public class ContentApi : Controller
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/content/{category}/{userId}/{hash}", GetRoute);
        app.MapGet("/content/{category}/{userId}/{hash}/signed", GetSignedUrlRoute);
    }
    
    private static async Task<IResult> GetSignedUrlRoute(CdnMemoryCache cache, ValourDb db, ContentCategory category, string hash, ulong userId)
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

        var url = await GetSignedUrlAsync(cache, bucketItem);
        if (string.IsNullOrWhiteSpace(url))
            return Results.BadRequest("Failed to generate pre-signed URL.");

        return ValourResult.Ok(url);
    }

    public static async Task<string> GetSignedUrlAsync(CdnMemoryCache cache, Valour.Database.CdnBucketItem bucketItem)
    {
        if (bucketItem is null || IsUnavailable(bucketItem))
            return null;

        var cacheKey = $"signed:{bucketItem.Id}:{bucketItem.MimeType}:{bucketItem.FileName}";

        if (cache.Cache.TryGetValue(cacheKey, out string cachedUrl))
            return cachedUrl;

        var request = new GetPreSignedUrlRequest()
        {
            Key = bucketItem.Hash,
            BucketName = "valourmps",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = bucketItem.MimeType,
                ContentDisposition = $"inline; filename=\"{bucketItem.FileName}\""
            }
        };

        var url = await CdnBucketService.Client.GetPreSignedURLAsync(request);
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

    private static async Task<IResult> GetRoute(CdnMemoryCache cache, ValourDb db,
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
            var request = new GetObjectRequest()
            {
                Key = hash,
                BucketName = "valourmps",
            };

            var response = await CdnBucketService.Client.GetObjectAsync(request);

            if (!CdnUtils.IsSuccessStatusCode(response.HttpStatusCode))
                return Results.BadRequest("Failed to get object from bucket.");

            MemoryStream ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);

            data = ms.ToArray();

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
