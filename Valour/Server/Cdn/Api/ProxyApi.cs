using Valour.Database;

namespace Valour.Server.Cdn.Api
{
    public static class ProxyApi
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/proxy/{url}", ProxyRoute);
        }

        /// <summary>
        /// The Proxy route proxies the page that corresponds with the given hash.
        /// </summary>
        private static async Task<IResult> ProxyRoute(HttpContext context, HttpClient client, ValourDb db, string url)
        {
            if (string.IsNullOrEmpty(url))
                return Results.BadRequest("Missing url parameter");

            CdnProxyItem item = await db.CdnProxyItems.FindAsync(url);

            if (item is null)
                return Results.NotFound("No existing proxy item found");

            return Results.Stream(await client.GetStreamAsync(item.Origin));
        }
    }
}
