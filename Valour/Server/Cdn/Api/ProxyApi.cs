using Microsoft.Extensions.Caching.Memory;
using Valour.Server.Cdn;

namespace Valour.Server.Cdn.Api
{
    public static class ProxyApi
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/proxy/{hash}", ProxyRoute);
        }

        /// <summary>
        /// The Proxy route proxies the page that corresponds with the given hash.
        /// </summary>
        private static async Task<IResult> ProxyRoute(
            HttpContext ctx,
            CdnMemoryCache cache,
            IHttpClientFactory clientFactory,
            ILoggerFactory loggerFactory,
            ValourDb db,
            string hash)
        {
            var logger = loggerFactory.CreateLogger("ProxyApi");

            if (string.IsNullOrEmpty(hash))
                return Results.BadRequest("Missing hash parameter");

            var item = await db.CdnProxyItems.FindAsync(hash);

            if (item is null)
                return Results.NotFound("No existing proxy item found");

            if (!await OutboundUrlSafetyValidator.IsSafeAsync(item.Origin, logger))
                return Results.Forbid();

            if (!Uri.TryCreate(item.Origin, UriKind.Absolute, out var originUri))
                return Results.NotFound("No existing proxy item found");

            // Extract filename from origin URL
            var fileName = Path.GetFileName(originUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = hash;

            // The name and type come from a third-party URL, so the same
            // inline/download rules as uploads apply.
            var headers = CdnServePolicy.Resolve(fileName, item.MimeType);
            CdnServePolicy.Apply(ctx.Response, headers);

            // Keys are namespaced per route so a proxy entry can never be
            // served as uploaded content or the reverse.
            var cacheKey = $"proxy:{hash}";

            // Try to get from cache
            if (cache.Cache.TryGetValue(cacheKey, out byte[] cachedData) && cachedData is not null)
                return Results.File(cachedData, headers.ContentType);

            var client = clientFactory.CreateClient("ProxyFetch");

            // Fetch from origin
            using var response = await client.GetAsync(originUri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);

            // This route is unauthenticated and the origin is third-party, so
            // the body must be bounded rather than buffered whole.
            var data = await CdnLimits.ReadBoundedAsync(response.Content, CdnLimits.MaxProxyResponseBytes);
            if (data is null)
                return Results.StatusCode(StatusCodes.Status502BadGateway);

            // Cache the data
            cache.Cache.Set(cacheKey, data, new MemoryCacheEntryOptions
            {
                Size = data.Length,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });

            return Results.File(data, headers.ContentType);
        }
    }
}
