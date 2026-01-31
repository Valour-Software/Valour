using Microsoft.Extensions.Caching.Memory;

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
        private static async Task<IResult> ProxyRoute(CdnMemoryCache cache, HttpContext context, HttpClient client, ValourDb db, string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return Results.BadRequest("Missing hash parameter");

            var item = await db.CdnProxyItems.FindAsync(hash);

            if (item is null)
                return Results.NotFound("No existing proxy item found");

            // Extract filename from origin URL
            var fileName = Path.GetFileName(new Uri(item.Origin).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = hash;

            var mimeType = item.MimeType ?? "application/octet-stream";

            // Try to get from cache
            if (cache.Cache.TryGetValue(hash, out var cachedData))
            {
                if (cachedData is not null)
                {
                    return Results.File((byte[])cachedData, mimeType, fileName);
                }
            }

            // Fetch from origin
            using var response = await client.GetAsync(item.Origin, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);

            var data = await response.Content.ReadAsByteArrayAsync();

            // Cache the data
            cache.Cache.Set(hash, data, new MemoryCacheEntryOptions
            {
                Size = data.Length,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });

            return Results.File(data, mimeType, fileName);
        }
    }
}
