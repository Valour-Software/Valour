using System.Collections.Concurrent;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using Amazon.S3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using Valour.Server.Cdn.Objects;

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

        var exists = await db.CdnBucketItems.AnyAsync(x => x.Id == id);
        if (!exists)
            return Results.NotFound();
        
        // Check for cached url
        if (cache.Cache.TryGetValue(hash, out string cachedUrl))
        {
            return ValourResult.Ok(cachedUrl);
        }
        
        // Generate a new pre-signed URL

        var request = new GetPreSignedUrlRequest()
        {
            Key = hash,
            BucketName = "valourmps",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET,
        };
        
        var url = await CdnBucketService.Client.GetPreSignedURLAsync(request);
        if (string.IsNullOrWhiteSpace(url))
            return Results.BadRequest("Failed to generate pre-signed URL.");
        
        // Cache the URL
        cache.Cache.Set(hash, url, 
            new MemoryCacheEntryOptions() 
            { 
                Size = hash.Length * sizeof(char), 
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) // This means worst case, the URL is valid for 30 minutes (best case, it is valid for 1 hour)
            }
        );
        
        return ValourResult.Ok(url);
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

        byte[] data;

        if (!cache.Cache.TryGetValue(hash, out data))
        {
            var request = new GetObjectRequest()
            {
                Key = hash,
                BucketName = "valourmps",
            };

            var response = await CdnBucketService.Client.GetObjectAsync(request);

            if (!IsSuccessStatusCode(response.HttpStatusCode))
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

    public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        var intStatus = (int)statusCode;
        return (intStatus >= 200) && (intStatus <= 299);
    }
}
