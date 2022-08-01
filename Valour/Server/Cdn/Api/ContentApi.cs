using Amazon.S3.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Valour.Server.Cdn.Objects;
using Valour.Shared;

namespace Valour.Server.Cdn.Api
{
    public class ContentApi
    {

        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/content/{category}/{userId}/{hash}", GetRoute);
        }

        private static async Task<IResult> GetRoute(IMemoryCache cache, HttpContext context, CdnDb db,
             ContentCategory category, string hash, ulong userId)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return Results.BadRequest("Include id.");

            if (userId == 0)
                return Results.BadRequest("Include user id.");

            var id = $"{category}/{userId}/{hash}";

            var bucketItemRecord = await db.BucketItems.FindAsync(id);
            if (bucketItemRecord is null)
                return Results.NotFound();

            var request = new GetObjectRequest()
            {
                Key = hash,
                BucketName = "valourmps"
            };

            var response = await BucketManager.Client.GetObjectAsync(request);

            if (!IsSuccessStatusCode(response.HttpStatusCode))
                return Results.BadRequest("Failed to get object from bucket.");

            return Results.Stream(response.ResponseStream, bucketItemRecord.MimeType, hash, enableRangeProcessing: true);
        }

        public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
        {
            var intStatus = (int)statusCode;
            return (intStatus >= 200) && (intStatus <= 299);
        }
    }
}
