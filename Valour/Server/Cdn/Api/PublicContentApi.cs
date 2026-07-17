using Valour.Server.Cdn.Storage;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Api;

/// <summary>
/// Serves public assets (avatars, planet icons, emoji, themes) directly from
/// the server under the /valour-public/ URL namespace. Only mapped when CDN
/// storage runs in filesystem mode — in s3 mode these URLs are served by the
/// external public CDN host.
/// </summary>
public class PublicContentApi
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/valour-public/{**path}", GetRoute);
    }

    private static async Task<IResult> GetRoute(HttpContext ctx, CdnStorageProvider storage, string path)
    {
        var download = await storage.Public.GetAsync(path);
        if (download is null)
            return Results.NotFound();

        CdnUtils.ExtensionToMimeType.TryGetValue(Path.GetExtension(path), out var mime);

        // Public asset URLs are versioned (?v=), so aggressive caching is safe.
        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        // Results.Stream takes ownership of the stream and disposes it.
        return Results.Stream(download.Stream, mime ?? "application/octet-stream");
    }
}
