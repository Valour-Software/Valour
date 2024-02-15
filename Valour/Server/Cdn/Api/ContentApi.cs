using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Web.Http;
using Valour.Server.Cdn.Objects;

namespace Valour.Server.Cdn.Api;

public class ContentApi : Controller
{

    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/content/{category}/{userId}/{hash}", GetRoute);
    }

    private static async Task<IResult> GetRoute(CdnMemoryCache cache, ValourDB db,
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

            var response = await BucketManager.Client.GetObjectAsync(request);

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
