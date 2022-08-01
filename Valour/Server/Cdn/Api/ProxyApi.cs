using System.Security.Cryptography;
using System.Text;
using Valour.Server.Cdn.Objects;

namespace Valour.Server.Cdn.Api
{
    public static class ProxyApi
    {
        static SHA256 SHA256 = SHA256.Create();

        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/proxy/{url}", ProxyRoute);
            app.MapPost("/proxy/create", CreateProxyRoute);
        }

        /// <summary>
        /// The Proxy route proxies the page that corresponds with the given hash.
        /// </summary>
        private static async Task<IResult> ProxyRoute(HttpContext context, HttpClient client, CdnDb db, string url)
        {
            if (string.IsNullOrEmpty(url))
                return Results.BadRequest("Missing url parameter");

            ProxyItem item = await db.ProxyItems.FindAsync(url);

            if (item is null)
                return Results.NotFound("No existing proxy item found");

            return Results.Stream(await client.GetStreamAsync(item.Origin));
        }

        /// <summary>
        /// The Proxy/SendUrl route allows a client to send a url and be returned a proxy item
        /// which can then be used to proxy the url in the future
        /// </summary>
        private static async Task<IResult> CreateProxyRoute(HttpContext context, HttpClient client, CdnDb db, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Results.BadRequest("Missing url");

            byte[] h = SHA256.ComputeHash(Encoding.UTF8.GetBytes(url));
            string hash = BitConverter.ToString(h).Replace("-", "").ToLower();

            ProxyItem item = await db.ProxyItems.FindAsync(hash);

            if (item is null)
            {
                // Check if end resource is media
                var response = await client.GetAsync(url);

                // If failure, return the reason and stop
                if (!response.IsSuccessStatusCode)
                    return Results.Problem("Proxy error: " + await response.Content.ReadAsStringAsync());

                IEnumerable<string> contentTypes;

                response.Content.Headers.TryGetValues("Content-Type", out contentTypes);

                string content_type = contentTypes.FirstOrDefault().Split(';')[0];

                item = new ProxyItem()
                {
                    Id = hash,
                    Origin = url,
                    MimeType = content_type
                };

                await db.AddAsync(item);
                await db.SaveChangesAsync();
            }

            return Results.Json(item);
        }
    }
}
